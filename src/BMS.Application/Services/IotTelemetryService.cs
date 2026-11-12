using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using BMS.Core.Exceptions;

namespace BMS.Application.Services;

/// <summary>
/// Production implementation of IoT Temperature &amp; Humidity Collection (UC65, UC66),
/// Connection Loss Detection &amp; Offline Buffer Recovery, and Sensor Log Analytics (UC69).
/// Conforms to: BR-IOT-01, BR-IOT-02, MSG-ERR-03, and AGENTS-CODE.md.
/// </summary>
public class IotTelemetryService : IIotTelemetryService
{
    private readonly IIotSensorLogRepository _logRepository;
    private readonly IIotThresholdRepository _thresholdRepository;
    private readonly IIotAlertService _alertService;
    private readonly ILogger<IotTelemetryService> _logger;

    private sealed class DeviceHeartbeatState
    {
        public string DeviceId { get; set; } = "BMS-IOT-001";
        public string DeviceName { get; set; } = "ESP32 DHT22 Tủ Bảo Quản Máu 01";
        public string Location { get; set; } = "Blood Storage Room 01";
        public DateTime? LastSeenAt { get; set; }
        public DateTime? LastPersistedAt { get; set; }
        public double? LastPersistedTemperature { get; set; }
        public double? LastPersistedHumidity { get; set; }
        public string? LastPersistedStatus { get; set; }
        public double? LastTemperature { get; set; }
        public double? LastHumidity { get; set; }
        public string LastStatus { get; set; } = "Normal";
        public bool ForceDisconnected { get; set; }
        public DateTime? ForcedDisconnectAt { get; set; }
        public bool HasReceivedLivePacket { get; set; }
        public string? ActiveOutageId { get; set; }
        public readonly SemaphoreSlim SyncLock = new(1, 1);
    }

    private static readonly ConcurrentDictionary<string, DeviceHeartbeatState> _heartbeats = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialBaselineSeeded;
    private static readonly object _seedLock = new();

    // Synchronized timing constants across IoT (5s send) -> Backend (30s grace timeout) -> Web Frontend (5s poll)
    private const int DefaultHardwareIntervalSeconds = 5;
    private const int MinHeartbeatTimeoutSeconds = 30;
    private const int PersistenceIntervalSeconds = 60;
    private const double TempDeadbandDelta = 0.5;
    private const double HumidityDeadbandDelta = 2.0;

    public IotTelemetryService(
        IIotSensorLogRepository logRepository,
        IIotThresholdRepository thresholdRepository,
        IIotAlertService alertService,
        ILogger<IotTelemetryService> logger)
    {
        _logRepository = logRepository;
        _thresholdRepository = thresholdRepository;
        _alertService = alertService;
        _logger = logger;
    }

    public async Task<IotTelemetryIngestResponseDto> IngestTelemetryAsync(IngestIotTelemetryRequestDto request, CancellationToken ct = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // Reject invalid packets before writing logs, heartbeat state or incident alerts.
        static bool ValidHumidity(double value) => double.IsFinite(value) && value is >= 0 and <= 100;
        var nowUtc = DateTime.UtcNow;
        if (request.Humidity.HasValue && !ValidHumidity(request.Humidity.Value)
            || request.Temperature.HasValue && !double.IsFinite(request.Temperature.Value)
            || request.AgeSeconds is < 0 or > 86400
            || request.Timestamp.HasValue && (request.Timestamp.Value.ToUniversalTime() > nowUtc.AddMinutes(2)
                || request.Timestamp.Value.ToUniversalTime() < nowUtc.AddDays(-1))
            || request.BufferedReadings is { Count: > 1000 }
            || request.BufferedReadings?.Any(x => !ValidHumidity(x.Humidity) || !double.IsFinite(x.Temperature)
                || x.AgeSeconds is < 0 or > 86400 || x.RecordedAt.HasValue &&
                    (x.RecordedAt.Value.ToUniversalTime() > nowUtc.AddMinutes(2) || x.RecordedAt.Value.ToUniversalTime() < nowUtc.AddDays(-1))) == true)
            throw new DomainValidationException("INVALID_IOT_READING", "Độ ẩm phải trong 0–100%, giá trị hữu hạn và thời gian/bộ đệm hợp lệ.");

        var deviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? "BMS-IOT-001" : request.DeviceId.Trim();
        var now = DateTime.UtcNow;

        var (minTemp, maxTemp, minHum, maxHum, intervalSec, resolvedName, resolvedLocation) = await GetEffectiveThresholdsAsync(deviceId);
        var deviceName = !string.IsNullOrWhiteSpace(request.DeviceName) ? request.DeviceName.Trim() : resolvedName;
        var location = !string.IsNullOrWhiteSpace(request.Location) ? request.Location.Trim() : resolvedLocation;

        var hb = _heartbeats.GetOrAdd(deviceId, _ => new DeviceHeartbeatState
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            Location = location
        });

        int timeoutSeconds = Math.Max(MinHeartbeatTimeoutSeconds, intervalSec * 5);

        string? resolvedOutageId = null;
        bool reconnectedFromOutage = false;
        int bufferedSyncedCount = 0;

        await hb.SyncLock.WaitAsync(ct);
        try
        {
            var activeOutage = await _logRepository.GetActiveOutageAsync(deviceId, ct);
            bool hadGapOutage = hb.HasReceivedLivePacket
                && hb.LastSeenAt.HasValue
                && (now - hb.LastSeenAt.Value).TotalSeconds > timeoutSeconds;
            bool hasBufferedItems = request.BufferedReadings != null && request.BufferedReadings.Count > 0;

            // Reconcile outage window if device was disconnected or flushed offline RAM buffer
            if (activeOutage != null || hb.ForceDisconnected || hadGapOutage || hasBufferedItems)
            {
                reconnectedFromOutage = true;
                DateTime disconnectedStart = activeOutage?.DisconnectedAt
                    ?? hb.ForcedDisconnectAt
                    ?? (hb.LastSeenAt.HasValue ? hb.LastSeenAt.Value.AddSeconds(intervalSec) : now.AddSeconds(-30));

                if (hasBufferedItems)
                {
                    var oldestAge = request.BufferedReadings!.Max(b => Math.Max(0, b.AgeSeconds));
                    var earliestBuffered = now.AddSeconds(-oldestAge - intervalSec);
                    if (earliestBuffered < disconnectedStart)
                    {
                        disconnectedStart = earliestBuffered;
                    }
                }

                var outage = activeOutage ?? new IotConnectionOutage
                {
                    Id = $"OUTAGE-{now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Location = location,
                    DisconnectedAt = disconnectedStart
                };

                outage.ReconnectedAt = now;
                outage.DurationSeconds = Math.Max(1, Math.Round((now - outage.DisconnectedAt).TotalSeconds, 1));
                outage.Status = "Reconnected";
                resolvedOutageId = outage.Id;

                if (hasBufferedItems)
                {
                    var bufferedLogs = new List<IotSensorLog>();
                    foreach (var buf in request.BufferedReadings!.OrderByDescending(b => b.AgeSeconds))
                    {
                        var sampleTime = buf.RecordedAt?.ToUniversalTime() ?? now.AddSeconds(-Math.Max(1, buf.AgeSeconds));
                        var bufStatus = EvaluateStatus(buf.Temperature, buf.Humidity, minTemp, maxTemp, minHum, maxHum);

                        bufferedLogs.Add(new IotSensorLog
                        {
                            DeviceId = deviceId,
                            DeviceName = deviceName,
                            Location = location,
                            TemperatureC = Math.Round(buf.Temperature, 2),
                            HumidityPercent = Math.Round(buf.Humidity, 2),
                            MinTempAllowed = minTemp,
                            MaxTempAllowed = maxTemp,
                            MinHumidityAllowed = minHum,
                            MaxHumidityAllowed = maxHum,
                            Status = bufStatus,
                            IsBuffered = true,
                            OutageId = outage.Id,
                            RecordedAt = sampleTime,
                            ReceivedAt = now
                        });
                    }

                    await _logRepository.InsertLogsBatchAsync(bufferedLogs, ct);
                    bufferedSyncedCount = bufferedLogs.Count;
                }

                outage.BufferedRecordsSynced += bufferedSyncedCount;
                outage.Notes = bufferedSyncedCount > 0
                    ? $"Đã kết nối lại thành công và đồng bộ bù {outage.BufferedRecordsSynced} bản ghi lưu trữ trong thời gian mất kết nối."
                    : "Thiết bị đã kết nối lại ổn định với máy chủ.";

                await _logRepository.UpsertOutageAsync(outage, ct);

                // Close any other stale "Disconnected" outage records for this device so status never flickers
                var allOutages = await _logRepository.GetOutagesAsync(deviceId, 20, ct);
                foreach (var stale in allOutages.Where(o => o.Status == "Disconnected" && o.Id != outage.Id))
                {
                    stale.ReconnectedAt = now;
                    stale.DurationSeconds = Math.Max(1, Math.Round((now - stale.DisconnectedAt).TotalSeconds, 1));
                    stale.Status = "Reconnected";
                    stale.Notes = "Đã đóng tự động khi thiết bị kết nối lại.";
                    await _logRepository.UpsertOutageAsync(stale, ct);
                }

                _logger.LogInformation("Thiết bị IoT {DeviceId} đã kết nối lại sau {Duration}s gián đoạn. Đồng bộ bù {Count} bản ghi.",
                    deviceId, outage.DurationSeconds, bufferedSyncedCount);
            }

            double currentTemp = Math.Round(request.Temperature ?? hb.LastTemperature ?? 4.5, 2);
            double currentHum = Math.Round(request.Humidity ?? hb.LastHumidity ?? 55.0, 2);
            DateTime recordedAt = request.Timestamp?.ToUniversalTime()
                ?? (request.AgeSeconds.HasValue && request.AgeSeconds.Value > 0 ? now.AddSeconds(-request.AgeSeconds.Value) : now);

            string status = EvaluateStatus(currentTemp, currentHum, minTemp, maxTemp, minHum, maxHum);
            bool shouldPersist = !hb.LastPersistedAt.HasValue
                || (now - hb.LastPersistedAt.Value).TotalSeconds >= PersistenceIntervalSeconds
                || reconnectedFromOutage
                || request.IsBuffered
                || hasBufferedItems
                || !status.Equals("Normal", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(hb.LastPersistedStatus, status, StringComparison.OrdinalIgnoreCase)
                || Math.Abs(currentTemp - (hb.LastPersistedTemperature ?? currentTemp)) >= TempDeadbandDelta
                || Math.Abs(currentHum - (hb.LastPersistedHumidity ?? currentHum)) >= HumidityDeadbandDelta;

            if (shouldPersist)
            {
                var logEntry = new IotSensorLog
                {
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Location = location,
                    TemperatureC = currentTemp,
                    HumidityPercent = currentHum,
                    MinTempAllowed = minTemp,
                    MaxTempAllowed = maxTemp,
                    MinHumidityAllowed = minHum,
                    MaxHumidityAllowed = maxHum,
                    Status = status,
                    IsBuffered = request.IsBuffered,
                    OutageId = request.IsBuffered ? resolvedOutageId : null,
                    RecordedAt = recordedAt,
                    ReceivedAt = now
                };

                await _logRepository.InsertLogAsync(logEntry, ct);

                hb.LastPersistedAt = now;
                hb.LastPersistedTemperature = currentTemp;
                hb.LastPersistedHumidity = currentHum;
                hb.LastPersistedStatus = status;
            }

            // Update live heartbeat state inside lock
            hb.DeviceName = deviceName;
            hb.Location = location;
            hb.LastSeenAt = now;
            hb.LastTemperature = currentTemp;
            hb.LastHumidity = currentHum;
            hb.LastStatus = status;
            hb.ForceDisconnected = false;
            hb.ForcedDisconnectAt = null;
            hb.HasReceivedLivePacket = true;
            hb.ActiveOutageId = null;
        }
        finally
        {
            hb.SyncLock.Release();
        }

        double finalTemp = hb.LastTemperature ?? 4.5;
        double finalHum = hb.LastHumidity ?? 55.0;
        string finalStatus = hb.LastStatus;
        DateTime finalRecordedAt = hb.LastSeenAt ?? now;

        // Evaluate against UC68 Alert Service for both Temperature and Humidity
        IotAlertDto? triggeredAlert = null;
        if (request.Temperature.HasValue)
        {
            var tempEval = await _alertService.EvaluateTelemetryAsync(new TelemetryReadingDto
            {
                DeviceId = deviceId,
                SensorType = "Temperature",
                Value = finalTemp,
                Timestamp = finalRecordedAt
            }, ct);
            if (tempEval.IsViolation && tempEval.Alert != null)
            {
                triggeredAlert = tempEval.Alert;
            }
        }

        if (request.Humidity.HasValue)
        {
            var humEval = await _alertService.EvaluateTelemetryAsync(new TelemetryReadingDto
            {
                DeviceId = deviceId,
                SensorType = "Humidity",
                Value = finalHum,
                Timestamp = finalRecordedAt
            }, ct);
            if (humEval.IsViolation && humEval.Alert != null && triggeredAlert == null)
            {
                triggeredAlert = humEval.Alert;
            }
        }

        bool isViolation = !finalStatus.Equals("Normal", StringComparison.OrdinalIgnoreCase);

        return new IotTelemetryIngestResponseDto
        {
            Success = true,
            DeviceId = deviceId,
            Location = location,
            Temperature = finalTemp,
            Humidity = finalHum,
            Status = finalStatus,
            IsViolation = isViolation,
            RecordedAt = finalRecordedAt,
            BufferedSyncedCount = bufferedSyncedCount,
            ReconnectedFromOutage = reconnectedFromOutage,
            OutageId = resolvedOutageId,
            MinTemp = minTemp,
            MaxTemp = maxTemp,
            MinHumidity = minHum,
            MaxHumidity = maxHum,
            IntervalSeconds = intervalSec,
            Message = isViolation
                ? $"MSG-WRN-04: BÁO ĐỘNG IOT: Tủ bảo quản {location} ({deviceId}) có chỉ số {finalTemp:F1}°C / {finalHum:F1}% ngoài ngưỡng an toàn!"
                : $"Đã ghi nhận dữ liệu IoT ({finalTemp:F1}°C, {finalHum:F1}%) thành công.",
            TriggeredAlert = triggeredAlert
        };
    }

    public async Task<IotConnectionStatusDto> GetConnectionStatusAsync(string? deviceId = null, CancellationToken ct = default)
    {
        await EnsureSeedHistoryIfEmptyAsync(ct);

        var targetDeviceId = string.IsNullOrWhiteSpace(deviceId) || deviceId.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? "BMS-IOT-001"
            : deviceId.Trim();

        var (_, _, _, _, intervalSec, deviceName, location) = await GetEffectiveThresholdsAsync(targetDeviceId);
        int timeoutSeconds = Math.Max(MinHeartbeatTimeoutSeconds, intervalSec * 5);
        var now = DateTime.UtcNow;

        var hb = _heartbeats.GetOrAdd(targetDeviceId, _ => new DeviceHeartbeatState
        {
            DeviceId = targetDeviceId,
            DeviceName = deviceName,
            Location = location
        });

        await hb.SyncLock.WaitAsync(ct);
        try
        {
            // Hydrate heartbeat from latest stored log if server just restarted
            if (!hb.LastSeenAt.HasValue)
            {
                var latestLog = await _logRepository.GetLatestLogByDeviceAsync(targetDeviceId, ct);
                if (latestLog != null)
                {
                    var latestTime = latestLog.ReceivedAt != default ? latestLog.ReceivedAt : latestLog.RecordedAt;
                    hb.LastSeenAt = latestTime;
                    hb.LastTemperature = latestLog.TemperatureC;
                    hb.LastHumidity = latestLog.HumidityPercent;
                    hb.LastStatus = latestLog.Status;
                    if ((now - latestTime).TotalSeconds <= timeoutSeconds)
                    {
                        hb.HasReceivedLivePacket = true;
                    }
                }
            }

            double? secondsSinceLastSeen = hb.LastSeenAt.HasValue
                ? Math.Max(0, Math.Round((now - hb.LastSeenAt.Value).TotalSeconds, 1))
                : null;

            bool isConnected = !hb.ForceDisconnected
                && secondsSinceLastSeen.HasValue
                && secondsSinceLastSeen.Value <= timeoutSeconds;

            var activeOutage = await _logRepository.GetActiveOutageAsync(targetDeviceId, ct);

            if (isConnected)
            {
                // If connected, ensure no stale "Disconnected" outage remains open in MongoDB
                if (activeOutage != null)
                {
                    activeOutage.ReconnectedAt = now;
                    activeOutage.DurationSeconds = Math.Max(1, Math.Round((now - activeOutage.DisconnectedAt).TotalSeconds, 1));
                    activeOutage.Status = "Reconnected";
                    activeOutage.Notes = "Đã đồng bộ kết nối trực tiếp từ thiết bị IoT.";
                    await _logRepository.UpsertOutageAsync(activeOutage, ct);
                    activeOutage = null;
                }
            }
            else if (hb.ForceDisconnected || hb.HasReceivedLivePacket)
            {
                // Only open a new outage record if the device was previously active in this session or explicitly disconnected
                if (activeOutage == null)
                {
                    DateTime disconnectedAt = hb.ForcedDisconnectAt
                        ?? (hb.LastSeenAt.HasValue ? hb.LastSeenAt.Value.AddSeconds(intervalSec) : now);

                    activeOutage = new IotConnectionOutage
                    {
                        Id = hb.ActiveOutageId ?? $"OUTAGE-{targetDeviceId}-{disconnectedAt:yyyyMMddHHmmss}",
                        DeviceId = targetDeviceId,
                        DeviceName = hb.DeviceName,
                        Location = hb.Location,
                        DisconnectedAt = disconnectedAt,
                        Status = "Disconnected",
                        DurationSeconds = Math.Max(1, Math.Round((now - disconnectedAt).TotalSeconds, 1)),
                        Notes = "Mất kết nối với thiết bị IoT ESP32. Đang chờ kết nối lại và đồng bộ dữ liệu lưu đệm."
                    };
                    hb.ActiveOutageId = activeOutage.Id;
                    await _logRepository.UpsertOutageAsync(activeOutage, ct);
                }
                else
                {
                    activeOutage.DurationSeconds = Math.Max(1, Math.Round((now - activeOutage.DisconnectedAt).TotalSeconds, 1));
                }
            }

            var recentOutages = await _logRepository.GetOutagesAsync(targetDeviceId, 5, ct);
            var lastRecovered = recentOutages.FirstOrDefault(o => o.Status == "Reconnected");

            return new IotConnectionStatusDto
            {
                DeviceId = targetDeviceId,
                DeviceName = hb.DeviceName,
                Location = hb.Location,
                IsConnected = isConnected,
                ConnectionState = isConnected ? "CONNECTED" : "DISCONNECTED",
                LastSeenAt = hb.LastSeenAt,
                SecondsSinceLastSeen = secondsSinceLastSeen,
                TimeoutThresholdSeconds = timeoutSeconds,
                DisconnectedSince = !isConnected ? (activeOutage?.DisconnectedAt ?? hb.LastSeenAt) : null,
                CurrentOutageSeconds = !isConnected
                    ? (activeOutage != null ? activeOutage.DurationSeconds : (secondsSinceLastSeen ?? 0))
                    : 0,
                AlertCode = isConnected ? null : "MSG-ERR-03",
                AlertMessage = isConnected
                    ? null
                    : "MSG-ERR-03: Mất kết nối đến Gateway cảm biến IoT ESP32. Không thể nhận dữ liệu thời gian thực!",
                LatestTemperature = hb.LastTemperature,
                LatestHumidity = hb.LastHumidity,
                LatestStatus = hb.LastStatus,
                ActiveOutage = activeOutage != null ? MapOutageToDto(activeOutage) : null,
                LastRecoveredOutage = lastRecovered != null ? MapOutageToDto(lastRecovered) : null
            };
        }
        finally
        {
            hb.SyncLock.Release();
        }
    }

    public async Task<IotMonitoringDashboardDto> GetMonitoringDashboardAsync(IotSensorLogQueryDto query, CancellationToken ct = default)
    {
        await EnsureSeedHistoryIfEmptyAsync(ct);

        var targetDeviceId = string.IsNullOrWhiteSpace(query.DeviceId) ? "BMS-IOT-001" : query.DeviceId.Trim();
        var connectionStatus = await GetConnectionStatusAsync(targetDeviceId, ct);
        var (minTemp, maxTemp, minHum, maxHum, intervalSec, _, _) = await GetEffectiveThresholdsAsync(targetDeviceId);

        DateTime? fromDate = query.FromDate;
        if (!fromDate.HasValue && query.Hours.HasValue && query.Hours.Value > 0)
        {
            fromDate = DateTime.UtcNow.AddHours(-query.Hours.Value);
        }

        bool? bufferedOnly = null;
        if (!string.IsNullOrWhiteSpace(query.Status) && query.Status.Equals("Buffered", StringComparison.OrdinalIgnoreCase))
        {
            bufferedOnly = true;
        }

        var allMatchingLogs = await _logRepository.GetLogsAsync(
            deviceId: targetDeviceId,
            fromDate: fromDate,
            toDate: query.ToDate,
            statusFilter: query.Status,
            isBufferedOnly: bufferedOnly,
            outageId: query.OutageId,
            searchTerm: query.Search,
            maxLimit: 1000,
            ct: ct);

        var outages = await _logRepository.GetOutagesAsync(targetDeviceId, 50, ct);
        var outageDtos = outages.Select(MapOutageToDto).ToList();

        var logDtos = allMatchingLogs.Select(MapLogToDto).ToList();

        // Chronological order (oldest -> newest) for SVG time-series chart rendering
        var chartSeries = logDtos
            .OrderBy(x => x.RecordedAt)
            .TakeLast(150)
            .ToList();

        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        int pageNumber = Math.Max(1, query.PageNumber);
        var pagedLogs = logDtos
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        double minT = logDtos.Count > 0 ? Math.Round(logDtos.Min(x => x.TemperatureC), 2) : 0;
        double maxT = logDtos.Count > 0 ? Math.Round(logDtos.Max(x => x.TemperatureC), 2) : 0;
        double avgT = logDtos.Count > 0 ? Math.Round(logDtos.Average(x => x.TemperatureC), 2) : 0;
        double mkt = CalculateMeanKineticTemperature(logDtos.Select(x => x.TemperatureC).ToList());

        double minH = logDtos.Count > 0 ? Math.Round(logDtos.Min(x => x.HumidityPercent), 2) : 0;
        double maxH = logDtos.Count > 0 ? Math.Round(logDtos.Max(x => x.HumidityPercent), 2) : 0;
        double avgH = logDtos.Count > 0 ? Math.Round(logDtos.Average(x => x.HumidityPercent), 2) : 0;

        var latestLog = logDtos.FirstOrDefault();

        return new IotMonitoringDashboardDto
        {
            Connection = connectionStatus,
            MinTempThreshold = minTemp,
            MaxTempThreshold = maxTemp,
            MinHumidityThreshold = minHum,
            MaxHumidityThreshold = maxHum,
            IntervalSeconds = intervalSec,
            LatestTemperature = connectionStatus.LatestTemperature ?? latestLog?.TemperatureC,
            LatestHumidity = connectionStatus.LatestHumidity ?? latestLog?.HumidityPercent,
            MinTemperature = minT,
            MaxTemperature = maxT,
            AvgTemperature = avgT,
            MeanKineticTemperature = mkt,
            MinHumidity = minH,
            MaxHumidity = maxH,
            AvgHumidity = avgH,
            TotalLogsCount = logDtos.Count,
            ViolationLogsCount = logDtos.Count(x => x.IsViolation),
            BufferedLogsCount = logDtos.Count(x => x.IsBuffered),
            OutagesCount = outageDtos.Count,
            ChartSeries = chartSeries,
            Outages = outageDtos,
            Logs = pagedLogs,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<List<IotConnectionOutageDto>> GetOutagesAsync(string? deviceId = null, CancellationToken ct = default)
    {
        var list = await _logRepository.GetOutagesAsync(deviceId, 100, ct);
        return list.Select(MapOutageToDto).ToList();
    }

    public async Task<IotConnectionStatusDto> ForceDisconnectStateAsync(string deviceId, string? reason = null, CancellationToken ct = default)
    {
        var targetDeviceId = string.IsNullOrWhiteSpace(deviceId) ? "BMS-IOT-001" : deviceId.Trim();
        var (_, _, _, _, _, deviceName, location) = await GetEffectiveThresholdsAsync(targetDeviceId);
        var now = DateTime.UtcNow;

        var hb = _heartbeats.GetOrAdd(targetDeviceId, _ => new DeviceHeartbeatState
        {
            DeviceId = targetDeviceId,
            DeviceName = deviceName,
            Location = location
        });

        await hb.SyncLock.WaitAsync(ct);
        try
        {
            hb.ForceDisconnected = true;
            hb.ForcedDisconnectAt = now;

            var activeOutage = await _logRepository.GetActiveOutageAsync(targetDeviceId, ct);
            if (activeOutage == null)
            {
                activeOutage = new IotConnectionOutage
                {
                    Id = $"OUTAGE-{targetDeviceId}-{now:yyyyMMddHHmmss}",
                    DeviceId = targetDeviceId,
                    DeviceName = hb.DeviceName,
                    Location = hb.Location,
                    DisconnectedAt = now,
                    Status = "Disconnected",
                    DurationSeconds = 1,
                    Notes = !string.IsNullOrWhiteSpace(reason)
                        ? reason
                        : "Mất kết nối giữa thiết bị IoT ESP32 và máy chủ BMS."
                };
                hb.ActiveOutageId = activeOutage.Id;
                await _logRepository.UpsertOutageAsync(activeOutage, ct);
            }
        }
        finally
        {
            hb.SyncLock.Release();
        }

        return await GetConnectionStatusAsync(targetDeviceId, ct);
    }

    public async Task<IotTelemetryIngestResponseDto> SimulateReconnectWithBufferedDataAsync(SimulateReconnectRequestDto request, CancellationToken ct = default)
    {
        var deviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? "BMS-IOT-001" : request.DeviceId.Trim();
        var activeOutage = await _logRepository.GetActiveOutageAsync(deviceId, ct);
        var now = DateTime.UtcNow;

        int durationSec = activeOutage != null
            ? Math.Clamp((int)(now - activeOutage.DisconnectedAt).TotalSeconds, 10, 600)
            : Math.Clamp(request.OutageDurationSeconds, 10, 600);

        int intervalSec = Math.Clamp(request.SampleIntervalSeconds, 2, 60);
        int count = Math.Clamp(durationSec / intervalSec, 2, 60);

        var bufferedList = new List<BufferedTelemetryItemDto>();
        for (int i = count; i >= 1; i--)
        {
            int age = i * intervalSec;
            double wave = Math.Sin(i * 0.6) * 0.4;
            bufferedList.Add(new BufferedTelemetryItemDto
            {
                Temperature = Math.Round(request.BaseTemperature + wave, 1),
                Humidity = Math.Round(request.BaseHumidity + (wave * 2.5), 1),
                AgeSeconds = age,
                RecordedAt = now.AddSeconds(-age)
            });
        }

        return await IngestTelemetryAsync(new IngestIotTelemetryRequestDto
        {
            DeviceId = deviceId,
            Location = request.Location,
            Temperature = Math.Round(request.BaseTemperature, 1),
            Humidity = Math.Round(request.BaseHumidity, 1),
            BufferedReadings = bufferedList
        }, ct);
    }

    public async Task<IotTelemetryIngestResponseDto> ReevaluateThresholdsAsync(string deviceId = "BMS-IOT-001", CancellationToken ct = default)
    {
        var targetDevice = string.IsNullOrWhiteSpace(deviceId) ? "BMS-IOT-001" : deviceId.Trim();
        var (minTemp, maxTemp, minHum, maxHum, intervalSec, deviceName, location) = await GetEffectiveThresholdsAsync(targetDevice);

        var latestLog = await _logRepository.GetLatestLogByDeviceAsync(targetDevice, ct);
        string status = "Normal";
        double temp = 4.5;
        double hum = 55.0;

        if (latestLog != null)
        {
            temp = latestLog.TemperatureC;
            hum = latestLog.HumidityPercent;
            status = EvaluateStatus(temp, hum, minTemp, maxTemp, minHum, maxHum);

            latestLog.MinTempAllowed = minTemp;
            latestLog.MaxTempAllowed = maxTemp;
            latestLog.MinHumidityAllowed = minHum;
            latestLog.MaxHumidityAllowed = maxHum;
            latestLog.Status = status;

            await _logRepository.InsertLogAsync(latestLog, ct);

            await _alertService.EvaluateTelemetryAsync(new TelemetryReadingDto
            {
                DeviceId = targetDevice,
                SensorType = "Temperature",
                Value = temp,
                Timestamp = DateTime.UtcNow
            }, ct);

            await _alertService.EvaluateTelemetryAsync(new TelemetryReadingDto
            {
                DeviceId = targetDevice,
                SensorType = "Humidity",
                Value = hum,
                Timestamp = DateTime.UtcNow
            }, ct);
        }

        if (_heartbeats.TryGetValue(targetDevice, out var hb))
        {
            hb.LastStatus = status;
        }

        return new IotTelemetryIngestResponseDto
        {
            DeviceId = targetDevice,
            Temperature = temp,
            Humidity = hum,
            Status = status,
            IsViolation = !status.Equals("Normal", StringComparison.OrdinalIgnoreCase),
            MinTemp = minTemp,
            MaxTemp = maxTemp,
            MinHumidity = minHum,
            MaxHumidity = maxHum,
            IntervalSeconds = intervalSec
        };
    }

    public async Task<int> RunDailyRollupAsync(DateTime? targetUtcDate = null, CancellationToken ct = default)
    {
        var dayStart = (targetUtcDate ?? DateTime.UtcNow).Date;
        var dayEnd = dayStart.AddDays(1).AddTicks(-1);

        var dayLogs = await _logRepository.GetLogsAsync(
            deviceId: null,
            fromDate: dayStart,
            toDate: dayEnd,
            maxLimit: 5000,
            ct: ct);

        if (dayLogs.Count == 0) return 0;

        var dayOutages = await _logRepository.GetOutagesAsync(deviceId: null, maxLimit: 500, ct: ct);
        var groupedByDevice = dayLogs.GroupBy(x => x.DeviceId, StringComparer.OrdinalIgnoreCase);

        int upsertedCount = 0;
        foreach (var group in groupedByDevice)
        {
            var items = group.ToList();
            if (items.Count == 0) continue;

            var first = items[0];
            var temps = items.Select(x => x.TemperatureC).ToList();
            int outageCount = dayOutages.Count(o =>
                o.DeviceId.Equals(first.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                o.DisconnectedAt >= dayStart &&
                o.DisconnectedAt <= dayEnd);

            var summary = new IotDailySummary
            {
                Id = $"{first.DeviceId}_{dayStart:yyyy-MM-dd}",
                DeviceId = first.DeviceId,
                DeviceName = first.DeviceName,
                Location = first.Location,
                SummaryDate = dayStart,
                MinTemp = Math.Round(temps.Min(), 2),
                MaxTemp = Math.Round(temps.Max(), 2),
                AvgTemp = Math.Round(temps.Average(), 2),
                MktTemp = CalculateMeanKineticTemperature(temps),
                MinHumidity = Math.Round(items.Min(x => x.HumidityPercent), 2),
                MaxHumidity = Math.Round(items.Max(x => x.HumidityPercent), 2),
                AvgHumidity = Math.Round(items.Average(x => x.HumidityPercent), 2),
                SampleCount = items.Count,
                ViolationCount = items.Count(x => !x.Status.Equals("Normal", StringComparison.OrdinalIgnoreCase)),
                OutageCount = outageCount,
                UpdatedAt = DateTime.UtcNow
            };

            await _logRepository.UpsertDailySummaryAsync(summary, ct);
            upsertedCount++;
        }

        return upsertedCount;
    }

    public async Task<List<IotDailySummaryDto>> GetDailySummariesAsync(
        string? deviceId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int maxLimit = 365,
        CancellationToken ct = default)
    {
        var items = await _logRepository.GetDailySummariesAsync(deviceId, fromDate, toDate, maxLimit, ct);
        return items.Select(x => new IotDailySummaryDto
        {
            Id = x.Id,
            DeviceId = x.DeviceId,
            DeviceName = x.DeviceName,
            Location = x.Location,
            SummaryDate = x.SummaryDate,
            MinTemp = x.MinTemp,
            MaxTemp = x.MaxTemp,
            AvgTemp = x.AvgTemp,
            MktTemp = x.MktTemp,
            MinHumidity = x.MinHumidity,
            MaxHumidity = x.MaxHumidity,
            AvgHumidity = x.AvgHumidity,
            SampleCount = x.SampleCount,
            ViolationCount = x.ViolationCount,
            OutageCount = x.OutageCount,
            UpdatedAt = x.UpdatedAt
        }).ToList();
    }

    #region Helpers

    private async Task EnsureSeedHistoryIfEmptyAsync(CancellationToken ct)
    {
        if (_initialBaselineSeeded) return;

        var existing = await _logRepository.GetLogsAsync(deviceId: "BMS-IOT-001", maxLimit: 5, ct: ct);
        if (existing.Count > 0)
        {
            _initialBaselineSeeded = true;
            return;
        }

        lock (_seedLock)
        {
            if (_initialBaselineSeeded) return;
            _initialBaselineSeeded = true;
        }

        var now = DateTime.UtcNow;
        var seedLogs = new List<IotSensorLog>();
        var outageId = "OUTAGE-SEED-001";

        var seedOutage = new IotConnectionOutage
        {
            Id = outageId,
            DeviceId = "BMS-IOT-001",
            DeviceName = "ESP32 DHT22 Tủ Bảo Quản Máu 01",
            Location = "Blood Storage Room 01",
            DisconnectedAt = now.AddMinutes(-25),
            ReconnectedAt = now.AddMinutes(-20),
            DurationSeconds = 300,
            BufferedRecordsSynced = 5,
            Status = "Reconnected",
            Notes = "Đồng bộ bù 5 bản ghi lưu trữ trong bộ nhớ đệm ESP32 khi khôi phục WiFi."
        };
        await _logRepository.UpsertOutageAsync(seedOutage, ct);

        double[] temps = [4.2, 4.4, 4.5, 4.3, 4.6, 4.8, 5.1, 5.3, 5.0, 4.7, 4.5, 4.3, 4.4, 4.6, 4.5];
        double[] hums = [54.0, 54.5, 55.2, 54.8, 56.0, 57.2, 58.5, 59.0, 57.8, 56.4, 55.5, 54.9, 55.1, 55.6, 55.0];

        for (int i = 0; i < temps.Length; i++)
        {
            var recordedTime = now.AddMinutes(-40 + (i * 2));
            bool isDuringSeedOutage = recordedTime >= seedOutage.DisconnectedAt && recordedTime <= seedOutage.ReconnectedAt;

            seedLogs.Add(new IotSensorLog
            {
                DeviceId = "BMS-IOT-001",
                DeviceName = "ESP32 DHT22 Tủ Bảo Quản Máu 01",
                Location = "Blood Storage Room 01",
                TemperatureC = temps[i],
                HumidityPercent = hums[i],
                MinTempAllowed = 2.0,
                MaxTempAllowed = 8.0,
                MinHumidityAllowed = 45.0,
                MaxHumidityAllowed = 75.0,
                Status = "Normal",
                IsBuffered = isDuringSeedOutage,
                OutageId = isDuringSeedOutage ? outageId : null,
                RecordedAt = recordedTime,
                ReceivedAt = isDuringSeedOutage ? seedOutage.ReconnectedAt!.Value : recordedTime
            });
        }

        await _logRepository.InsertLogsBatchAsync(seedLogs, ct);
    }

    private async Task<(double MinTemp, double MaxTemp, double MinHum, double MaxHum, int IntervalSec, string DeviceName, string Location)> GetEffectiveThresholdsAsync(string deviceId)
    {
        double minTemp = 2.0, maxTemp = 8.0;
        double minHum = 45.0, maxHum = 75.0;
        int intervalSec = DefaultHardwareIntervalSeconds;
        string deviceName = "ESP32 DHT22 Tủ Bảo Quản Máu 01";
        string location = "Blood Storage Room 01";

        var tempConfig = await _thresholdRepository.GetThresholdByDeviceIdAsync(deviceId);
        if (tempConfig != null)
        {
            if (tempConfig.SensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
            {
                minTemp = tempConfig.LowAlert;
                maxTemp = tempConfig.HighAlert;
            }
            else if (tempConfig.SensorType.Equals("Humidity", StringComparison.OrdinalIgnoreCase))
            {
                minHum = tempConfig.LowAlert;
                maxHum = tempConfig.HighAlert;
            }

            // Keep physical ESP32 gateway BMS-IOT-001 on a fast 5s cadence (clamped 3s-10s) so real-time charts stay live
            intervalSec = deviceId.Equals("BMS-IOT-001", StringComparison.OrdinalIgnoreCase)
                ? Math.Clamp(tempConfig.IntervalSeconds, 3, 10)
                : Math.Max(5, tempConfig.IntervalSeconds);
            deviceName = tempConfig.DeviceName;
            location = tempConfig.Location;
        }

        var humDeviceId = deviceId.Equals("BMS-IOT-001", StringComparison.OrdinalIgnoreCase) ? "BMS-IOT-001-HUM" : "HUM-001";
        var humConfig = await _thresholdRepository.GetThresholdByDeviceIdAsync(humDeviceId);
        if (humConfig != null && humConfig.SensorType.Equals("Humidity", StringComparison.OrdinalIgnoreCase))
        {
            minHum = humConfig.LowAlert;
            maxHum = humConfig.HighAlert;
        }

        return (minTemp, maxTemp, minHum, maxHum, intervalSec, deviceName, location);
    }

    private static string EvaluateStatus(double temp, double hum, double minTemp, double maxTemp, double minHum, double maxHum)
    {
        if (temp > maxTemp) return "Alert_High_Temp";
        if (temp < minTemp) return "Alert_Low_Temp";
        if (hum > maxHum) return "Alert_High_Humidity";
        if (hum < minHum) return "Alert_Low_Humidity";
        return "Normal";
    }

    private static double CalculateMeanKineticTemperature(List<double> temperaturesC)
    {
        if (temperaturesC == null || temperaturesC.Count == 0) return 0;

        const double deltaH = 83.14472;
        const double gasConstantR = 0.008314472;
        const double ratio = deltaH / gasConstantR;

        double sumExp = 0;
        foreach (var tC in temperaturesC)
        {
            double kelvin = tC + 273.15;
            if (kelvin <= 0) kelvin = 273.15;
            sumExp += Math.Exp(-ratio / kelvin);
        }

        double meanExp = sumExp / temperaturesC.Count;
        if (meanExp <= 0) return Math.Round(temperaturesC.Average(), 2);

        double mktKelvin = ratio / -Math.Log(meanExp);
        return Math.Round(mktKelvin - 273.15, 2);
    }

    private static IotSensorLogDto MapLogToDto(IotSensorLog entity)
    {
        return new IotSensorLogDto
        {
            Id = entity.Id,
            DeviceId = entity.DeviceId,
            DeviceName = entity.DeviceName,
            Location = entity.Location,
            TemperatureC = entity.TemperatureC,
            HumidityPercent = entity.HumidityPercent,
            MinTempAllowed = entity.MinTempAllowed,
            MaxTempAllowed = entity.MaxTempAllowed,
            MinHumidityAllowed = entity.MinHumidityAllowed,
            MaxHumidityAllowed = entity.MaxHumidityAllowed,
            Status = entity.Status,
            IsViolation = !entity.Status.Equals("Normal", StringComparison.OrdinalIgnoreCase),
            IsBuffered = entity.IsBuffered,
            OutageId = entity.OutageId,
            RecordedAt = entity.RecordedAt,
            ReceivedAt = entity.ReceivedAt
        };
    }

    private static IotConnectionOutageDto MapOutageToDto(IotConnectionOutage outage)
    {
        double duration = outage.Status == "Disconnected"
            ? Math.Max(1, Math.Round((DateTime.UtcNow - outage.DisconnectedAt).TotalSeconds, 1))
            : outage.DurationSeconds;

        return new IotConnectionOutageDto
        {
            Id = outage.Id,
            DeviceId = outage.DeviceId,
            DeviceName = outage.DeviceName,
            Location = outage.Location,
            DisconnectedAt = outage.DisconnectedAt,
            ReconnectedAt = outage.ReconnectedAt,
            DurationSeconds = duration,
            BufferedRecordsSynced = outage.BufferedRecordsSynced,
            Status = outage.Status,
            Notes = outage.Notes
        };
    }

    #endregion
}
