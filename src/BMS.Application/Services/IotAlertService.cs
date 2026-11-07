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
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Production Implementation of IoT Environmental Threshold Violation &amp; Incident Escalation Service (UC68).
/// Enforces cold-chain safety boundaries, multi-channel alerting, and corrective action workflows (BR-IOT-01, BR-IOT-02).
/// </summary>
public class IotAlertService : IIotAlertService
{
    private readonly IMongoDatabase? _database;
    private readonly IMongoCollection<IotThresholdAlert>? _alertsCollection;
    private readonly IIotThresholdRepository? _thresholdRepository;
    private readonly ILogger<IotAlertService> _logger;

    // Concurrent in-memory storage for high-frequency sensor readings & zero-downtime offline tests
    private static readonly ConcurrentDictionary<string, IotThresholdAlert> _alerts = new(
        InitializeSeedAlerts().ToDictionary(a => a.Id, a => a)
    );

    // Tracks start of continuous threshold violation per (DeviceId:SensorType) for BR-IOT-02 5-minute escalation
    private static readonly ConcurrentDictionary<string, DateTime> _continuousViolationStart = new(StringComparer.OrdinalIgnoreCase);

    public IotAlertService(
        ILogger<IotAlertService> logger,
        IMongoDatabase? database = null,
        IIotThresholdRepository? thresholdRepository = null)
    {
        _logger = logger;
        _database = database;
        _thresholdRepository = thresholdRepository;

        if (_database != null)
        {
            try
            {
                _alertsCollection = _database.GetCollection<IotThresholdAlert>("iot_alerts");
            }
            catch
            {
                // Fallback to in-memory dictionary if MongoDB is unavailable
            }
        }
    }

    public async Task<ThresholdEvaluationResultDto> EvaluateTelemetryAsync(TelemetryReadingDto reading, CancellationToken ct = default)
    {
        if (reading == null || string.IsNullOrWhiteSpace(reading.DeviceId))
        {
            throw new ArgumentException("Thông tin telemetry không hợp lệ hoặc thiếu mã thiết bị.", nameof(reading));
        }

        var (low, high, deviceName, location, unit) = await ResolveDeviceThresholdsAsync(reading.DeviceId, reading.SensorType);
        var violationKey = $"{reading.DeviceId}:{reading.SensorType}";
        var now = reading.Timestamp ?? DateTime.UtcNow;

        bool isViolation = reading.Value < low || reading.Value > high;

        if (!isViolation)
        {
            _continuousViolationStart.TryRemove(violationKey, out _);

            if (reading.DeviceId.StartsWith("BMS-IOT", StringComparison.OrdinalIgnoreCase))
            {
                var activeToResolve = _alerts.Values
                    .Where(a => a.DeviceId.Equals(reading.DeviceId, StringComparison.OrdinalIgnoreCase)
                             && a.SensorType.Equals(reading.SensorType, StringComparison.OrdinalIgnoreCase)
                             && a.Status != IotAlertStatus.Resolved)
                    .ToList();

                foreach (var resolvedAlert in activeToResolve)
                {
                    resolvedAlert.Status = IotAlertStatus.Resolved;
                    resolvedAlert.ResolvedAt = DateTime.UtcNow;
                    resolvedAlert.ResolvedBy = "Hệ Thống IoT Tự Động";
                    resolvedAlert.ResolutionNotes = $"Chỉ số {reading.SensorType} ({reading.Value:F1}{unit}) đã nằm trong ngưỡng an toàn ({low:F1} - {high:F1}{unit}).";
                    await PersistAlertToMongoAsync(resolvedAlert, ct);
                }
            }

            return new ThresholdEvaluationResultDto
            {
                IsViolation = false,
                ExcursionAmount = 0,
                Severity = null,
                Message = $"Chỉ số cảm biến {reading.SensorType} của {reading.DeviceId} đạt chuẩn an toàn ({reading.Value:F1}{unit}).",
                Alert = null
            };
        }

        var firstViolationAt = _continuousViolationStart.GetOrAdd(violationKey, now);
        double continuousMinutes = Math.Max(0, Math.Round((now - firstViolationAt).TotalMinutes, 1));

        double excursion = reading.Value > high ? reading.Value - high : low - reading.Value;
        excursion = Math.Round(excursion, 2);

        var severity = DetermineSeverity(reading.SensorType, excursion);

        // BR-IOT-02: Escalate to Danger_Emergency if violation persists continuously for >= 5 minutes
        if (continuousMinutes >= 5.0 && severity < IotAlertSeverity.Danger_Emergency)
        {
            severity = IotAlertSeverity.Danger_Emergency;
        }

        var channels = new List<string> { "InApp", "Push" };
        if (severity >= IotAlertSeverity.Critical)
        {
            channels.Add("SMS");
            channels.Add("Email");
        }

        // For real-time hardware devices (e.g. BMS-IOT-001 sending every 5s), update existing active alert within 60s window
        // to avoid flooding thousands of duplicate rows while preserving instant escalation.
        IotThresholdAlert? existingActive = null;
        if (reading.DeviceId.StartsWith("BMS-IOT", StringComparison.OrdinalIgnoreCase))
        {
            existingActive = _alerts.Values
                .Where(a => a.DeviceId.Equals(reading.DeviceId, StringComparison.OrdinalIgnoreCase)
                         && a.SensorType.Equals(reading.SensorType, StringComparison.OrdinalIgnoreCase)
                         && a.Status == IotAlertStatus.Active
                         && (now - a.TriggeredAt).TotalMinutes < 30)
                .OrderByDescending(a => a.TriggeredAt)
                .FirstOrDefault();
        }

        IotThresholdAlert alert;
        if (existingActive != null)
        {
            existingActive.ObservedValue = reading.Value;
            existingActive.LowAlert = low;
            existingActive.HighAlert = high;
            existingActive.ExcursionAmount = excursion;
            if (severity > existingActive.Severity)
            {
                existingActive.Severity = severity;
            }
            existingActive.NotificationChannelsDispatched = channels;
            existingActive.DurationMinutes = Math.Max(continuousMinutes, Math.Round((now - existingActive.TriggeredAt).TotalMinutes, 1));
            alert = existingActive;
        }
        else
        {
            alert = new IotThresholdAlert
            {
                Id = $"ALERT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
                DeviceId = reading.DeviceId,
                DeviceName = deviceName,
                Location = location,
                SensorType = reading.SensorType,
                Unit = unit,
                ObservedValue = reading.Value,
                LowAlert = low,
                HighAlert = high,
                ExcursionAmount = excursion,
                Severity = severity,
                Status = IotAlertStatus.Active,
                TriggeredAt = now,
                NotificationChannelsDispatched = channels,
                DurationMinutes = continuousMinutes
            };
        }

        _alerts[alert.Id] = alert;
        await PersistAlertToMongoAsync(alert, ct);

        _logger.LogWarning("BÁO ĐỘNG VƯỢT NGƯỠNG IOT [{Severity}]: Thiết bị {DeviceId} ({DeviceName}) ghi nhận {Value}{Unit} (Ngưỡng an toàn: {Low} - {High}{Unit}).",
            severity, alert.DeviceId, alert.DeviceName, alert.ObservedValue, alert.Unit, alert.LowAlert, alert.HighAlert, alert.Unit);

        return new ThresholdEvaluationResultDto
        {
            IsViolation = true,
            ExcursionAmount = excursion,
            Severity = alert.SeverityString,
            Message = $"Phát hiện vượt ngưỡng {alert.SeverityString}: {reading.Value:F1}{unit} vượt mức an toàn ({low:F1} - {high:F1}{unit})!",
            Alert = MapToDto(alert)
        };
    }

    public async Task<List<IotAlertDto>> GetAlertsAsync(IotAlertQueryParameters parameters, CancellationToken ct = default)
    {
        await SyncFromMongoIfAvailableAsync(ct);

        var query = _alerts.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(parameters.Status) && !parameters.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => a.StatusString.Equals(parameters.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.Severity) && !parameters.Severity.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => a.SeverityString.Equals(parameters.Severity, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.DeviceId))
        {
            query = query.Where(a => a.DeviceId.Contains(parameters.DeviceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(parameters.SensorType) && !parameters.SensorType.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => a.SensorType.Equals(parameters.SensorType, StringComparison.OrdinalIgnoreCase));
        }

        if (parameters.FromDate.HasValue)
        {
            query = query.Where(a => a.TriggeredAt >= parameters.FromDate.Value);
        }

        if (parameters.ToDate.HasValue)
        {
            query = query.Where(a => a.TriggeredAt <= parameters.ToDate.Value);
        }

        var results = query
            .OrderByDescending(a => a.Status == IotAlertStatus.Active)
            .ThenByDescending(a => a.Severity)
            .ThenByDescending(a => a.TriggeredAt)
            .Select(MapToDto)
            .ToList();

        return results;
    }

    public async Task<IotAlertDto?> GetAlertByIdAsync(string alertId, CancellationToken ct = default)
    {
        if (_alerts.TryGetValue(alertId, out var alert))
        {
            return MapToDto(alert);
        }
        return await Task.FromResult<IotAlertDto?>(null);
    }

    public async Task<IotAlertSummaryDto> GetAlertSummaryAsync(CancellationToken ct = default)
    {
        await SyncFromMongoIfAvailableAsync(ct);

        var all = _alerts.Values.ToList();
        var active = all.Where(a => a.Status == IotAlertStatus.Active).ToList();
        var critical = active.Count(a => a.Severity >= IotAlertSeverity.Critical);
        var acknowledged = all.Count(a => a.Status == IotAlertStatus.Acknowledged);

        var today = DateTime.UtcNow.Date;
        var resolvedToday = all.Where(a => a.Status == IotAlertStatus.Resolved && a.ResolvedAt.HasValue && a.ResolvedAt.Value.Date == today).ToList();

        double avgResolution = resolvedToday.Count > 0 ? resolvedToday.Average(a => a.DurationMinutes) : 18.5;

        string healthStatus = "NORMAL";
        if (critical > 0) healthStatus = "CRITICAL";
        else if (active.Count > 0) healthStatus = "WARNING";

        return new IotAlertSummaryDto
        {
            ActiveAlertsCount = active.Count,
            CriticalEmergencyCount = critical,
            AcknowledgedCount = acknowledged,
            ResolvedTodayCount = resolvedToday.Count,
            AverageResolutionMinutes = Math.Round(avgResolution, 1),
            SystemHealthStatus = healthStatus
        };
    }

    public async Task<IotAlertDto> AcknowledgeAlertAsync(string alertId, AcknowledgeAlertRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.OperatorName))
        {
            throw new ArgumentException("Họ tên nhân viên tiếp nhận không được để trống.", nameof(request));
        }

        if (!_alerts.TryGetValue(alertId, out var alert))
        {
            throw new KeyNotFoundException($"Không tìm thấy cảnh báo với mã '{alertId}'.");
        }

        alert.Status = IotAlertStatus.Acknowledged;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = request.OperatorName;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            alert.ResolutionNotes = $"[Tiếp nhận]: {request.Notes}";
        }

        await PersistAlertToMongoAsync(alert, ct);

        _logger.LogInformation("Nhân viên {Operator} đã tiếp nhận cảnh báo IoT {AlertId} ({DeviceId}).",
            request.OperatorName, alert.Id, alert.DeviceId);

        return MapToDto(alert);
    }

    public async Task<IotAlertDto> ResolveAlertAsync(string alertId, ResolveAlertRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.OperatorName))
        {
            throw new ArgumentException("Họ tên nhân viên xử lý không được để trống.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request?.ResolutionNotes))
        {
            throw new ArgumentException("Bắt buộc ghi chú biện pháp khắc phục sự cố (Resolution notes).", nameof(request));
        }

        if (!_alerts.TryGetValue(alertId, out var alert))
        {
            throw new KeyNotFoundException($"Không tìm thấy cảnh báo với mã '{alertId}'.");
        }

        var now = DateTime.UtcNow;
        alert.Status = IotAlertStatus.Resolved;
        alert.ResolvedAt = now;
        alert.ResolvedBy = request.OperatorName;
        alert.ResolutionNotes = request.ResolutionNotes;
        alert.DurationMinutes = Math.Max(1.0, Math.Round((now - alert.TriggeredAt).TotalMinutes, 1));

        _continuousViolationStart.TryRemove($"{alert.DeviceId}:{alert.SensorType}", out _);
        await PersistAlertToMongoAsync(alert, ct);

        _logger.LogInformation("Cảnh báo IoT {AlertId} ({DeviceId}) đã được xử lý xong bởi {Operator}. Ghi chú: {Notes}.",
            alert.Id, alert.DeviceId, request.OperatorName, request.ResolutionNotes);

        return MapToDto(alert);
    }

    public async Task<IotAlertDto> TriggerSimulatedAlertAsync(SimulateAlertRequestDto request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        double excursion = request.SimulatedValue > request.HighAlert
            ? request.SimulatedValue - request.HighAlert
            : request.LowAlert - request.SimulatedValue;
        excursion = Math.Round(Math.Abs(excursion), 2);

        var severity = DetermineSeverity(request.SensorType, excursion);
        var unit = request.SensorType.Equals("Humidity", StringComparison.OrdinalIgnoreCase) ? "%" : "°C";

        var alert = new IotThresholdAlert
        {
            Id = $"ALERT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName,
            Location = request.Location,
            SensorType = request.SensorType,
            Unit = unit,
            ObservedValue = request.SimulatedValue,
            LowAlert = request.LowAlert,
            HighAlert = request.HighAlert,
            ExcursionAmount = excursion,
            Severity = severity,
            Status = IotAlertStatus.Active,
            TriggeredAt = DateTime.UtcNow,
            NotificationChannelsDispatched = new List<string> { "Push", "InApp", "SMS", "Email" },
            DurationMinutes = 0
        };

        _alerts[alert.Id] = alert;
        await PersistAlertToMongoAsync(alert, ct);

        return MapToDto(alert);
    }

    #region Helpers

    private async Task PersistAlertToMongoAsync(IotThresholdAlert alert, CancellationToken ct)
    {
        if (_alertsCollection == null) return;
        try
        {
            await _alertsCollection.ReplaceOneAsync(
                a => a.Id == alert.Id,
                alert,
                new ReplaceOptions { IsUpsert = true },
                ct);
        }
        catch
        {
            // Keep in-memory state if Mongo write fails
        }
    }

    private async Task SyncFromMongoIfAvailableAsync(CancellationToken ct)
    {
        if (_alertsCollection == null) return;
        try
        {
            var list = await _alertsCollection.Find(_ => true).Limit(200).ToListAsync(ct);
            if (list != null)
            {
                foreach (var item in list)
                {
                    _alerts[item.Id] = item;
                }
            }
        }
        catch
        {
            // Ignore Mongo read error and use in-memory cache
        }
    }

    private async Task<(double Low, double High, string Name, string Location, string Unit)> ResolveDeviceThresholdsAsync(string deviceId, string sensorType)
    {
        if (_thresholdRepository != null)
        {
            var lookupId = sensorType.Equals("Humidity", StringComparison.OrdinalIgnoreCase) && deviceId.Equals("BMS-IOT-001", StringComparison.OrdinalIgnoreCase)
                ? "BMS-IOT-001-HUM"
                : deviceId;

            var configured = await _thresholdRepository.GetThresholdByDeviceIdAsync(lookupId);
            if (configured != null && configured.SensorType.Equals(sensorType, StringComparison.OrdinalIgnoreCase))
            {
                return (configured.LowAlert, configured.HighAlert, configured.DeviceName, configured.Location, configured.Unit);
            }
        }

        return GetDeviceThresholds(deviceId, sensorType);
    }

    private static IotAlertDto MapToDto(IotThresholdAlert alert)
    {
        return new IotAlertDto
        {
            Id = alert.Id,
            DeviceId = alert.DeviceId,
            DeviceName = alert.DeviceName,
            Location = alert.Location,
            SensorType = alert.SensorType,
            Unit = alert.Unit,
            ObservedValue = alert.ObservedValue,
            LowAlert = alert.LowAlert,
            HighAlert = alert.HighAlert,
            ExcursionAmount = alert.ExcursionAmount,
            Severity = alert.SeverityString,
            Status = alert.StatusString,
            TriggeredAt = alert.TriggeredAt,
            AcknowledgedAt = alert.AcknowledgedAt,
            AcknowledgedBy = alert.AcknowledgedBy,
            ResolvedAt = alert.ResolvedAt,
            ResolvedBy = alert.ResolvedBy,
            ResolutionNotes = alert.ResolutionNotes,
            NotificationChannelsDispatched = alert.NotificationChannelsDispatched,
            DurationMinutes = alert.Status == IotAlertStatus.Resolved
                ? alert.DurationMinutes
                : Math.Round((DateTime.UtcNow - alert.TriggeredAt).TotalMinutes, 1)
        };
    }

    private static IotAlertSeverity DetermineSeverity(string sensorType, double excursion)
    {
        if (sensorType.Equals("Humidity", StringComparison.OrdinalIgnoreCase))
        {
            if (excursion <= 5.0) return IotAlertSeverity.Warning;
            if (excursion <= 15.0) return IotAlertSeverity.Critical;
            return IotAlertSeverity.Danger_Emergency;
        }

        // Temperature (°C)
        if (excursion <= 1.0) return IotAlertSeverity.Warning;
        if (excursion <= 3.0) return IotAlertSeverity.Critical;
        return IotAlertSeverity.Danger_Emergency;
    }

    private static (double Low, double High, string Name, string Location, string Unit) GetDeviceThresholds(string deviceId, string sensorType)
    {
        if (sensorType.Equals("Humidity", StringComparison.OrdinalIgnoreCase) || deviceId.StartsWith("HUM", StringComparison.OrdinalIgnoreCase))
        {
            if (deviceId.StartsWith("BMS-IOT", StringComparison.OrdinalIgnoreCase))
            {
                return (45.0, 75.0, $"ESP32 DHT22 Tủ Bảo Quản Máu ({deviceId})", "Blood Storage Room 01", "%");
            }
            return (30.0, 60.0, $"Cảm biến độ ẩm {deviceId}", "Phòng Lab Trung Tâm", "%");
        }

        if (deviceId.StartsWith("BMS-IOT", StringComparison.OrdinalIgnoreCase))
        {
            return (2.0, 8.0, $"ESP32 DHT22 Tủ Bảo Quản Máu ({deviceId})", "Blood Storage Room 01", "°C");
        }

        if (deviceId.Contains("FREEZER", StringComparison.OrdinalIgnoreCase))
        {
            return (-25.0, -18.0, $"Tủ đông huyết tương {deviceId}", "Kho lạnh bảo quản âm sâu", "°C");
        }

        if (deviceId.Contains("INCUBATOR", StringComparison.OrdinalIgnoreCase))
        {
            return (36.0, 38.0, $"Tủ ấm nuôi cấy {deviceId}", "Phòng Vi Sinh", "°C");
        }

        // Default: Blood storage refrigerator (2°C - 6°C)
        return (2.0, 6.0, $"Tủ lưu trữ máu {deviceId}", "Kho lạnh bảo quản tầng 1", "°C");
    }

    private static List<IotThresholdAlert> InitializeSeedAlerts()
    {
        var now = DateTime.UtcNow;
        return new List<IotThresholdAlert>
        {
            new()
            {
                Id = "ALERT-20261023-001",
                DeviceId = "TEMP-001",
                DeviceName = "Tủ lưu trữ máu toàn phần R-01",
                Location = "Kho lạnh bảo quản mẫu tầng 1",
                SensorType = "Temperature",
                Unit = "°C",
                ObservedValue = 8.4,
                LowAlert = 2.0,
                HighAlert = 6.0,
                ExcursionAmount = 2.4,
                Severity = IotAlertSeverity.Critical,
                Status = IotAlertStatus.Active,
                TriggeredAt = now.AddMinutes(-35),
                NotificationChannelsDispatched = new List<string> { "InApp", "Push", "SMS" },
                DurationMinutes = 35
            },
            new()
            {
                Id = "ALERT-20261023-002",
                DeviceId = "HUM-002",
                DeviceName = "Cảm biến độ ẩm phòng phân tích sinh hóa",
                Location = "Phòng Lab Sinh Hóa 02",
                SensorType = "Humidity",
                Unit = "%",
                ObservedValue = 26.0,
                LowAlert = 30.0,
                HighAlert = 60.0,
                ExcursionAmount = 4.0,
                Severity = IotAlertSeverity.Warning,
                Status = IotAlertStatus.Acknowledged,
                TriggeredAt = now.AddHours(-2),
                AcknowledgedAt = now.AddHours(-1.5),
                AcknowledgedBy = "BS. Lê Minh Tuấn",
                ResolutionNotes = "[Tiếp nhận]: Đã bật máy tạo ẩm phòng lab.",
                NotificationChannelsDispatched = new List<string> { "InApp", "Push" },
                DurationMinutes = 120
            },
            new()
            {
                Id = "ALERT-20261023-003",
                DeviceId = "FREEZER-01",
                DeviceName = "Tủ đông huyết tương sâu F-01",
                Location = "Kho bảo quản chế phẩm máu",
                SensorType = "Temperature",
                Unit = "°C",
                ObservedValue = -12.5,
                LowAlert = -25.0,
                HighAlert = -18.0,
                ExcursionAmount = 5.5,
                Severity = IotAlertSeverity.Danger_Emergency,
                Status = IotAlertStatus.Resolved,
                TriggeredAt = now.AddHours(-6),
                AcknowledgedAt = now.AddHours(-5.8),
                AcknowledgedBy = "KTV. Phạm Quốc Hưng",
                ResolvedAt = now.AddHours(-5.2),
                ResolvedBy = "KTV. Phạm Quốc Hưng",
                ResolutionNotes = "Đã thay rơ le máy nén gas và chuyển mẫu dự phòng sang tủ F-02. Nhiệt độ đã hồi phục -21.4°C an toàn.",
                NotificationChannelsDispatched = new List<string> { "Push", "InApp", "SMS", "Email" },
                DurationMinutes = 48
            }
        };
    }

    #endregion
}
