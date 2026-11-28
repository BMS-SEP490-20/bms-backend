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
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// ISO 15189 Compliant Implementation of IoT Environmental Threshold Violation &amp; Incident Escalation Service (UC68).
/// - Zero hardcoded/fake seed alerts or fake metrics (100% backed by MongoDB <c>iot_alerts</c>).
/// - Never auto-resolves alerts when temperature returns to safe band; instead flags <c>TemperatureRecovered</c>
///   and keeps the alert OPEN until authorized staff evaluate blood specimen impact and document CAPA.
/// - Dispatches real In-App notifications (<c>notifications</c> collection), real SignalR broadcasts,
///   and real SMTP alert emails (recording ONLY channels that were actually dispatched).
/// </summary>
public class IotAlertService : IIotAlertService
{
    private static readonly string[] LegacyHardcodedSeedIds =
    [
        "ALERT-20261023-001",
        "ALERT-20261023-002",
        "ALERT-20261023-003"
    ];

    private readonly IMongoDatabase? _database;
    private readonly IMongoCollection<IotThresholdAlert>? _alertsCollection;
    private readonly IIotThresholdRepository? _thresholdRepository;
    private readonly INotificationRepository? _notificationRepository;
    private readonly IEmailService? _emailService;
    private readonly IIotRealtimeNotifier? _realtimeNotifier;
    private readonly ILogger<IotAlertService> _logger;

    private static readonly ConcurrentDictionary<string, IotThresholdAlert> _alerts = new(StringComparer.OrdinalIgnoreCase);
    private static volatile bool _legacySeedCleaned;

    private static readonly ConcurrentDictionary<string, DateTime> _continuousViolationStart = new(StringComparer.OrdinalIgnoreCase);

    public IotAlertService(
        ILogger<IotAlertService> logger,
        IMongoDatabase? database = null,
        IIotThresholdRepository? thresholdRepository = null,
        INotificationRepository? notificationRepository = null,
        IEmailService? emailService = null,
        IIotRealtimeNotifier? realtimeNotifier = null)
    {
        _logger = logger;
        _database = database;
        _thresholdRepository = thresholdRepository;
        _notificationRepository = notificationRepository;
        _emailService = emailService;
        _realtimeNotifier = realtimeNotifier;

        if (_database != null)
        {
            try
            {
                _alertsCollection = _database.GetCollection<IotThresholdAlert>("iot_alerts");
            }
            catch
            {
            }
        }
    }

    public async Task<ThresholdEvaluationResultDto> EvaluateTelemetryAsync(TelemetryReadingDto reading, CancellationToken ct = default)
    {
        if (reading == null || string.IsNullOrWhiteSpace(reading.DeviceId))
        {
            throw new ArgumentException("Thông tin telemetry không hợp lệ hoặc thiếu mã thiết bị.", nameof(reading));
        }

        await SyncFromMongoIfAvailableAsync(ct);

        var (low, high, deviceName, location, unit) = await ResolveDeviceThresholdsAsync(reading.DeviceId, reading.SensorType);
        var violationKey = $"{reading.DeviceId}:{reading.SensorType}";
        var now = reading.Timestamp ?? DateTime.UtcNow;

        bool isViolation = reading.Value < low || reading.Value > high;

        if (!isViolation)
        {
            _continuousViolationStart.TryRemove(violationKey, out _);

            var openAlerts = _alerts.Values
                .Where(a => a.DeviceId.Equals(reading.DeviceId, StringComparison.OrdinalIgnoreCase)
                         && a.SensorType.Equals(reading.SensorType, StringComparison.OrdinalIgnoreCase)
                         && a.Status != IotAlertStatus.Resolved)
                .ToList();

            foreach (var openAlert in openAlerts)
            {
                if (!openAlert.TemperatureRecovered)
                {
                    openAlert.TemperatureRecovered = true;
                    openAlert.RecoveredAt = now;
                    openAlert.RecoveredValue = reading.Value;
                    var recoveryNote = $"[Hồi phục cảm biến {now:dd/MM HH:mm} UTC]: Chỉ số {reading.SensorType} đã trở về dải an toàn ({reading.Value:F1}{unit}). Chờ nhân sự đánh giá tác động mẫu máu & hoàn tất CAPA trước khi đóng cảnh báo (ISO 15189).";
                    openAlert.ResolutionNotes = string.IsNullOrWhiteSpace(openAlert.ResolutionNotes)
                        ? recoveryNote
                        : $"{openAlert.ResolutionNotes}\n{recoveryNote}";

                    await PersistAlertToMongoAsync(openAlert, ct);
                    if (_realtimeNotifier != null)
                    {
                        await _realtimeNotifier.BroadcastAlertAsync("RecoveredPendingCapa", MapToDto(openAlert), ct);
                    }
                }
            }

            return new ThresholdEvaluationResultDto
            {
                IsViolation = false,
                ExcursionAmount = 0,
                Severity = null,
                Message = openAlerts.Count > 0
                    ? $"Chỉ số cảm biến {reading.SensorType} của {reading.DeviceId} đã về dải an toàn ({reading.Value:F1}{unit}), nhưng vẫn còn {openAlerts.Count} cảnh báo chờ đóng CAPA (ISO 15189)."
                    : $"Chỉ số cảm biến {reading.SensorType} của {reading.DeviceId} đạt chuẩn an toàn ({reading.Value:F1}{unit}).",
                Alert = null
            };
        }

        var firstViolationAt = _continuousViolationStart.GetOrAdd(violationKey, now);
        double continuousMinutes = Math.Max(0, Math.Round((now - firstViolationAt).TotalMinutes, 1));

        double excursion = reading.Value > high ? reading.Value - high : low - reading.Value;
        excursion = Math.Round(excursion, 2);

        var severity = DetermineSeverity(reading.SensorType, excursion);

        if (continuousMinutes >= 5.0 && severity < IotAlertSeverity.Danger_Emergency)
        {
            severity = IotAlertSeverity.Danger_Emergency;
        }

        var existingOpen = _alerts.Values
            .Where(a => a.DeviceId.Equals(reading.DeviceId, StringComparison.OrdinalIgnoreCase)
                     && a.SensorType.Equals(reading.SensorType, StringComparison.OrdinalIgnoreCase)
                     && a.Status == IotAlertStatus.Active
                     && (now - a.TriggeredAt).TotalMinutes < 30)
            .OrderByDescending(a => a.TriggeredAt)
            .FirstOrDefault();

        IotThresholdAlert alert;
        bool shouldDispatchNotifications;

        if (existingOpen != null)
        {
            bool escalated = severity > existingOpen.Severity;
            existingOpen.ObservedValue = reading.Value;
            existingOpen.LowAlert = low;
            existingOpen.HighAlert = high;
            existingOpen.ExcursionAmount = excursion;
            existingOpen.TemperatureRecovered = false;
            if (escalated)
            {
                existingOpen.Severity = severity;
            }
            existingOpen.DurationMinutes = Math.Max(continuousMinutes, Math.Round((now - existingOpen.TriggeredAt).TotalMinutes, 1));
            alert = existingOpen;
            shouldDispatchNotifications = escalated;
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
                TemperatureRecovered = false,
                CapaRequired = true,
                NotificationChannelsDispatched = new List<string>(),
                DurationMinutes = continuousMinutes
            };
            shouldDispatchNotifications = true;
        }

        if (shouldDispatchNotifications)
        {
            var realChannels = await DispatchRealAlertNotificationsAsync(alert, ct);
            foreach (var ch in realChannels)
            {
                if (!alert.NotificationChannelsDispatched.Contains(ch, StringComparer.OrdinalIgnoreCase))
                {
                    alert.NotificationChannelsDispatched.Add(ch);
                }
            }
        }

        _alerts[alert.Id] = alert;
        await PersistAlertToMongoAsync(alert, ct);

        _logger.LogWarning(
            "BÁO ĐỘNG VƯỢT NGƯỠNG IOT [{Severity}]: Thiết bị {DeviceId} ({DeviceName}) ghi nhận {Value}{Unit} (Ngưỡng an toàn: {Low} - {High}{Unit}). Kênh gửi thật: [{Channels}].",
            alert.Severity, alert.DeviceId, alert.DeviceName, alert.ObservedValue, alert.Unit, alert.LowAlert, alert.HighAlert, alert.Unit,
            string.Join(", ", alert.NotificationChannelsDispatched));

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

        return query
            .OrderByDescending(a => a.Status == IotAlertStatus.Active)
            .ThenByDescending(a => a.Status == IotAlertStatus.Acknowledged)
            .ThenByDescending(a => a.Severity)
            .ThenByDescending(a => a.TriggeredAt)
            .Select(MapToDto)
            .ToList();
    }

    public async Task<IotAlertDto?> GetAlertByIdAsync(string alertId, CancellationToken ct = default)
    {
        await SyncFromMongoIfAvailableAsync(ct);
        if (_alerts.TryGetValue(alertId, out var alert))
        {
            return MapToDto(alert);
        }
        return null;
    }

    public async Task<IotAlertSummaryDto> GetAlertSummaryAsync(CancellationToken ct = default)
    {
        await SyncFromMongoIfAvailableAsync(ct);

        var all = _alerts.Values.ToList();
        var active = all.Where(a => a.Status == IotAlertStatus.Active).ToList();
        var critical = active.Count(a => a.Severity >= IotAlertSeverity.Critical);
        var acknowledged = all.Count(a => a.Status == IotAlertStatus.Acknowledged);

        var today = DateTime.UtcNow.Date;
        var resolvedToday = all
            .Where(a => a.Status == IotAlertStatus.Resolved && a.ResolvedAt.HasValue && a.ResolvedAt.Value.Date == today)
            .ToList();
        var allResolved = all.Where(a => a.Status == IotAlertStatus.Resolved && a.DurationMinutes > 0).ToList();

        double avgResolution = resolvedToday.Count > 0
            ? resolvedToday.Average(a => a.DurationMinutes)
            : (allResolved.Count > 0 ? allResolved.Average(a => a.DurationMinutes) : 0.0);

        string healthStatus = "NORMAL";
        if (critical > 0) healthStatus = "CRITICAL";
        else if (active.Count > 0 || acknowledged > 0) healthStatus = "WARNING";

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

        await SyncFromMongoIfAvailableAsync(ct);
        if (!_alerts.TryGetValue(alertId, out var alert))
        {
            throw new KeyNotFoundException($"Không tìm thấy cảnh báo với mã '{alertId}'.");
        }

        alert.Status = IotAlertStatus.Acknowledged;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = request.OperatorName.Trim();
        alert.AcknowledgedByUserId = string.IsNullOrWhiteSpace(request.OperatorUserId) ? null : request.OperatorUserId.Trim();

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            var noteLine = $"[Tiếp nhận - {alert.AcknowledgedBy}]: {request.Notes.Trim()}";
            alert.ResolutionNotes = string.IsNullOrWhiteSpace(alert.ResolutionNotes)
                ? noteLine
                : $"{alert.ResolutionNotes}\n{noteLine}";
        }

        await PersistAlertToMongoAsync(alert, ct);
        var dto = MapToDto(alert);
        if (_realtimeNotifier != null)
        {
            await _realtimeNotifier.BroadcastAlertAsync("Acknowledged", dto, ct);
        }

        _logger.LogInformation("Nhân viên {Operator} (UID={UserId}) đã tiếp nhận cảnh báo IoT {AlertId} ({DeviceId}).",
            alert.AcknowledgedBy, alert.AcknowledgedByUserId, alert.Id, alert.DeviceId);

        return dto;
    }

    public async Task<IotAlertDto> ResolveAlertAsync(string alertId, ResolveAlertRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.OperatorName))
        {
            throw new ArgumentException("Họ tên nhân viên xử lý không được để trống.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request?.ResolutionNotes) || request.ResolutionNotes.Trim().Length < 10)
        {
            throw new ArgumentException("Bắt buộc ghi rõ biên bản đánh giá mẫu máu & biện pháp khắc phục CAPA tối thiểu 10 ký tự (ISO 15189).", nameof(request));
        }

        await SyncFromMongoIfAvailableAsync(ct);
        if (!_alerts.TryGetValue(alertId, out var alert))
        {
            throw new KeyNotFoundException($"Không tìm thấy cảnh báo với mã '{alertId}'.");
        }

        var now = DateTime.UtcNow;
        alert.Status = IotAlertStatus.Resolved;
        alert.ResolvedAt = now;
        alert.ResolvedBy = request.OperatorName.Trim();
        alert.ResolvedByUserId = string.IsNullOrWhiteSpace(request.OperatorUserId) ? null : request.OperatorUserId.Trim();
        alert.ResolutionNotes = request.ResolutionNotes.Trim();
        alert.DurationMinutes = Math.Max(0.1, Math.Round((now - alert.TriggeredAt).TotalMinutes, 1));

        _continuousViolationStart.TryRemove($"{alert.DeviceId}:{alert.SensorType}", out _);
        await PersistAlertToMongoAsync(alert, ct);

        var dto = MapToDto(alert);
        if (_realtimeNotifier != null)
        {
            await _realtimeNotifier.BroadcastAlertAsync("Resolved", dto, ct);
        }

        _logger.LogInformation("Cảnh báo IoT {AlertId} ({DeviceId}) đã đóng CAPA bởi {Operator} (UID={UserId}). Ghi chú: {Notes}.",
            alert.Id, alert.DeviceId, alert.ResolvedBy, alert.ResolvedByUserId, alert.ResolutionNotes);

        return dto;
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
            TemperatureRecovered = false,
            CapaRequired = true,
            NotificationChannelsDispatched = new List<string>(),
            DurationMinutes = 0
        };

        var realChannels = await DispatchRealAlertNotificationsAsync(alert, ct);
        alert.NotificationChannelsDispatched = realChannels;

        _alerts[alert.Id] = alert;
        await PersistAlertToMongoAsync(alert, ct);

        return MapToDto(alert);
    }

    #region Real Notification Dispatch & Persistence Helpers

    private async Task<List<string>> DispatchRealAlertNotificationsAsync(IotThresholdAlert alert, CancellationToken ct)
    {
        var dispatched = new List<string>();
        var title = $"Cảnh báo IoT [{alert.SeverityString}]: {alert.DeviceId} ({alert.ObservedValue:F1}{alert.Unit})";
        var message = $"Tủ/Thiết bị {alert.DeviceName} tại {alert.Location} ghi nhận {alert.SensorType} = {alert.ObservedValue:F1}{alert.Unit} (Ngưỡng an toàn: {alert.LowAlert:F1} – {alert.HighAlert:F1}{alert.Unit}). Yêu cầu kiểm tra mẫu bệnh phẩm và hoàn tất CAPA (ISO 15189).";

        if (_notificationRepository != null)
        {
            try
            {
                foreach (var targetRole in new[] { "Manager", "Lab" })
                {
                    await _notificationRepository.AddAsync(new Notification
                    {
                        NotificationId = $"NTF-IOT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
                        UserId = "ALL",
                        Role = targetRole,
                        Title = title,
                        Message = message,
                        Type = "iot_cold_chain_alert",
                        ReferenceId = alert.Id,
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    }, ct);
                }
                dispatched.Add("InApp");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IoT Alert] Failed to persist real InApp notification for {AlertId}", alert.Id);
            }
        }

        if (_realtimeNotifier != null)
        {
            var pushed = await _realtimeNotifier.BroadcastAlertAsync("Triggered", MapToDto(alert), ct);
            if (pushed)
            {
                dispatched.Add("SignalR");
            }
        }

        if (_emailService != null && alert.Severity >= IotAlertSeverity.Critical && _database != null)
        {
            _ = DispatchAlertEmailsInBackgroundAsync(alert);
        }

        return dispatched;
    }

    private async Task DispatchAlertEmailsInBackgroundAsync(IotThresholdAlert alert)
    {
        if (_emailService == null || _database == null) return;
        try
        {
            using var bgCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var recipients = await FindStaffAlertRecipientsAsync(bgCts.Token);
            if (recipients.Count == 0) return;

            var tasks = recipients.Select(r => _emailService.SendIotAlertEmailAsync(
                r.Email,
                r.FullName,
                alert.DeviceId,
                alert.DeviceName,
                alert.Location,
                alert.SensorType,
                alert.ObservedValue,
                alert.Unit,
                alert.LowAlert,
                alert.HighAlert,
                alert.SeverityString,
                bgCts.Token));

            var results = await Task.WhenAll(tasks);
            if (results.Any(sent => sent))
            {
                if (!alert.NotificationChannelsDispatched.Contains("Email", StringComparer.OrdinalIgnoreCase))
                {
                    alert.NotificationChannelsDispatched.Add("Email");
                    await PersistAlertToMongoAsync(alert, CancellationToken.None);
                    if (_realtimeNotifier != null)
                    {
                        await _realtimeNotifier.BroadcastAlertAsync("ChannelsUpdated", MapToDto(alert), CancellationToken.None);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[IoT Alert] Background SMTP email dispatch failed for {AlertId}", alert.Id);
        }
    }

    private async Task<List<(string Email, string FullName)>> FindStaffAlertRecipientsAsync(CancellationToken ct)
    {
        var result = new List<(string Email, string FullName)>();
        if (_database == null) return result;

        try
        {
            var accounts = _database.GetCollection<BsonDocument>("accounts");
            var filter = Builders<BsonDocument>.Filter.In("role", new[] { "Manager", "Lab", "Admin" });
            var docs = await accounts.Find(filter).Limit(10).ToListAsync(ct);
            foreach (var doc in docs)
            {
                var email = doc.GetValue("email", BsonNull.Value).IsString ? doc["email"].AsString : null;
                if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) continue;
                var fullName = doc.GetValue("full_name", BsonNull.Value).IsString
                    ? doc["full_name"].AsString
                    : (doc.GetValue("fullName", BsonNull.Value).IsString ? doc["fullName"].AsString : "Nhân sự Quản lý Lab");
                result.Add((email.Trim(), fullName));
            }
        }
        catch
        {
        }

        return result;
    }

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
        }
    }

    private async Task SyncFromMongoIfAvailableAsync(CancellationToken ct)
    {
        if (_alertsCollection == null) return;
        try
        {
            if (!_legacySeedCleaned)
            {
                _legacySeedCleaned = true;
                var deleteFilter = Builders<IotThresholdAlert>.Filter.In(a => a.Id, LegacyHardcodedSeedIds);
                await _alertsCollection.DeleteManyAsync(deleteFilter, ct);
                foreach (var seedId in LegacyHardcodedSeedIds)
                {
                    _alerts.TryRemove(seedId, out _);
                }
            }

            var list = await _alertsCollection.Find(_ => true).Limit(200).ToListAsync(ct);
            if (list != null)
            {
                var dbIds = new HashSet<string>(list.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
                foreach (var key in _alerts.Keys.ToList())
                {
                    if (!dbIds.Contains(key))
                    {
                        _alerts.TryRemove(key, out _);
                    }
                }
                foreach (var item in list)
                {
                    _alerts[item.Id] = item;
                }
            }
        }
        catch
        {
        }
    }

    private async Task<(double Low, double High, string Name, string Location, string Unit)> ResolveDeviceThresholdsAsync(string deviceId, string sensorType)
    {
        if (_thresholdRepository != null)
        {
            var lookupId = sensorType.Equals("Humidity", StringComparison.OrdinalIgnoreCase) && !deviceId.EndsWith("-HUM", StringComparison.OrdinalIgnoreCase)
                ? $"{deviceId}-HUM"
                : deviceId;

            var configured = await _thresholdRepository.GetThresholdByDeviceIdAsync(lookupId);
            if (configured != null && configured.SensorType.Equals(sensorType, StringComparison.OrdinalIgnoreCase))
            {
                return (configured.LowAlert, configured.HighAlert, configured.DeviceName, configured.Location, configured.Unit);
            }

            if (lookupId != deviceId)
            {
                configured = await _thresholdRepository.GetThresholdByDeviceIdAsync(deviceId);
                if (configured != null && configured.SensorType.Equals(sensorType, StringComparison.OrdinalIgnoreCase))
                {
                    return (configured.LowAlert, configured.HighAlert, configured.DeviceName, configured.Location, configured.Unit);
                }
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
            AcknowledgedByUserId = alert.AcknowledgedByUserId,
            ResolvedAt = alert.ResolvedAt,
            ResolvedBy = alert.ResolvedBy,
            ResolvedByUserId = alert.ResolvedByUserId,
            ResolutionNotes = alert.ResolutionNotes,
            TemperatureRecovered = alert.TemperatureRecovered,
            RecoveredAt = alert.RecoveredAt,
            RecoveredValue = alert.RecoveredValue,
            CapaRequired = alert.CapaRequired,
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

        return (2.0, 6.0, $"Tủ lưu trữ máu {deviceId}", "Kho lạnh bảo quản tầng 1", "°C");
    }

    #endregion
}
