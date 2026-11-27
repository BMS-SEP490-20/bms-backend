using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Notifications;
using BMS.Application.DTOs.Roles;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Service implementation for Laboratory Diagnostic Test Result Notifications (UC60 / BR-182).
/// Dispatches test result emails directly via SMTP, with automatic Panic Value detection and emergency escalation.
/// Conforms to: Production Medical Standards, ISO 15189:2022, ISO 27789:2021, OWASP ASVS & AGENTS-CODE.md.
/// </summary>
public class TestResultNotificationService : ITestResultNotificationService
{
    private readonly ITestResultNotificationRepository _repository;
    private readonly IEmailService _emailService;
    private readonly ILogger<TestResultNotificationService> _logger;
    private readonly IMongoDatabase? _db;
    private readonly IPasswordHasher? _passwordHasher;

    public TestResultNotificationService(
        ITestResultNotificationRepository repository,
        IEmailService emailService,
        ILogger<TestResultNotificationService> logger,
        IMongoDatabase? db = null,
        IPasswordHasher? passwordHasher = null)
    {
        _repository = repository;
        _emailService = emailService;
        _logger = logger;
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task<(bool Success, string Message, TestResultNotificationDetailDto? Data)> DispatchNotificationAsync(
        DispatchTestResultNotificationDto dto,
        string operatorId,
        string operatorName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.TestOrderId))
        {
            return (false, "Mã đơn chỉ định xét nghiệm (TestOrderId) không được để trống.", null);
        }

        if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.PatientName))
        {
            return (false, "Thông tin bệnh nhân (Mã và Họ tên) là bắt buộc.", null);
        }

        // Workflow 9 sync: hệ thống không sử dụng kênh SMS (chỉ Email + In-App/Push)
        if (dto.Channel == TestResultNotificationChannel.Sms)
        {
            return (false, "Hệ thống không hỗ trợ gửi thông báo qua SMS (theo phạm vi Workflow 9 chỉ dùng Email và Thông báo ứng dụng).", null);
        }

        // =====================================================================
        // RULE 1: Only notify if test result is APPROVED by Doctor in test_results (ISO 15189:2022)
        // =====================================================================
        if (_db != null)
        {
            var testResultsCol = _db.GetCollection<BsonDocument>("test_results");
            var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(dto.TestOrderId)) candidateIds.Add(dto.TestOrderId.Trim());
            if (!string.IsNullOrWhiteSpace(dto.SpecimenBarcode)) candidateIds.Add(dto.SpecimenBarcode.Trim());

            // Resolve linked sample_id / appointment_id from blood_samples ONLY to look up the corresponding test_results record
            var samplesCol = _db.GetCollection<BsonDocument>("blood_samples");
            var sampleLookupFilters = new List<FilterDefinition<BsonDocument>>();
            foreach (var cid in candidateIds.ToList())
            {
                sampleLookupFilters.Add(Builders<BsonDocument>.Filter.Eq("_id", cid));
                sampleLookupFilters.Add(Builders<BsonDocument>.Filter.Eq("barcode", cid));
                sampleLookupFilters.Add(Builders<BsonDocument>.Filter.Eq("appointment_id", cid));
                sampleLookupFilters.Add(Builders<BsonDocument>.Filter.Eq("test_order_id", cid));
                sampleLookupFilters.Add(Builders<BsonDocument>.Filter.Eq("order_id", cid));
            }

            if (sampleLookupFilters.Count > 0)
            {
                var linkedSamples = await samplesCol.Find(Builders<BsonDocument>.Filter.Or(sampleLookupFilters)).ToListAsync(ct);
                foreach (var sDoc in linkedSamples)
                {
                    foreach (var field in new[] { "_id", "barcode", "appointment_id", "test_order_id", "order_id" })
                    {
                        if (sDoc.Contains(field) && !sDoc[field].IsBsonNull)
                        {
                            var val = sDoc[field].IsString ? sDoc[field].AsString : sDoc[field].ToString();
                            if (!string.IsNullOrWhiteSpace(val)) candidateIds.Add(val.Trim());
                        }
                    }
                }
            }

            var idArray = candidateIds.ToArray();
            var resultFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Ne("isSuperseded", true),
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.In("_id", idArray),
                    Builders<BsonDocument>.Filter.In("testResultId", idArray),
                    Builders<BsonDocument>.Filter.In("appointmentId", idArray),
                    Builders<BsonDocument>.Filter.In("appointment_id", idArray),
                    Builders<BsonDocument>.Filter.In("test_order_id", idArray),
                    Builders<BsonDocument>.Filter.In("testOrderId", idArray),
                    Builders<BsonDocument>.Filter.In("orderId", idArray),
                    Builders<BsonDocument>.Filter.In("sampleId", idArray),
                    Builders<BsonDocument>.Filter.In("sample_id", idArray)
                )
            );

            var matchingResults = await testResultsCol.Find(resultFilter).ToListAsync(ct);
            if (matchingResults.Count == 0)
            {
                return (false, $"Không tìm thấy kết quả xét nghiệm đã được Bác sĩ phê duyệt cho mã đơn '{dto.TestOrderId}'.", null);
            }

            var approvedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Approved", "Published", "Signed"
            };

            var hasDoctorApprovedResult = matchingResults.Any(r =>
            {
                if (!r.Contains("status") || r["status"].IsBsonNull) return false;
                var st = r["status"].IsString ? r["status"].AsString.Trim() : r["status"].ToString()?.Trim();
                return !string.IsNullOrWhiteSpace(st) && approvedStatuses.Contains(st);
            });

            if (!hasDoctorApprovedResult)
            {
                return (false, "Kết quả xét nghiệm chưa được Bác sĩ phê duyệt. Chỉ những đơn đã được Bác sĩ duyệt kết quả mới được phép gửi thông báo qua Email cho bệnh nhân.", null);
            }

            // =====================================================================
            // RULE 2: Recipient Account Binding Verification (OWASP ASVS / Anti-mismatch)
            // =====================================================================
            var patientsCol = _db.GetCollection<Patient>("patients");
            var pFilter = Builders<Patient>.Filter.Or(
                Builders<Patient>.Filter.Eq(x => x.Id, dto.PatientId.Trim()),
                Builders<Patient>.Filter.Eq(x => x.PatientCodeField, dto.PatientId.Trim())
            );
            var patient = await patientsCol.Find(pFilter).FirstOrDefaultAsync(ct);
            if (patient != null)
            {
                if (!string.IsNullOrWhiteSpace(dto.PatientEmail))
                {
                    var isDirectEmailMatch = string.Equals(patient.Email?.Trim(), dto.PatientEmail.Trim(), StringComparison.OrdinalIgnoreCase);
                    var isAccountEmailMatch = false;

                    if (!isDirectEmailMatch && !string.IsNullOrWhiteSpace(patient.AccountId))
                    {
                        var usersCol = _db.GetCollection<User>("accounts");
                        var uFilter = Builders<User>.Filter.Eq(x => x.Id, patient.AccountId.Trim());
                        var linkedUser = await usersCol.Find(uFilter).FirstOrDefaultAsync(ct);
                        if (linkedUser != null && string.Equals(linkedUser.Email?.Trim(), dto.PatientEmail.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            isAccountEmailMatch = true;
                        }
                    }

                    if (!isDirectEmailMatch && !isAccountEmailMatch)
                    {
                        return (false, $"Địa chỉ email người nhận '{dto.PatientEmail}' không liên kết với hồ sơ bệnh nhân '{dto.PatientName}' ({dto.PatientId}). Kiểm tra liên kết tài khoản thất bại.", null);
                    }
                }

                if (!string.IsNullOrWhiteSpace(dto.PatientPhone) && !string.IsNullOrWhiteSpace(patient.PhoneNumber))
                {
                    var p1 = new string(patient.PhoneNumber.Where(char.IsDigit).ToArray());
                    var p2 = new string(dto.PatientPhone.Where(char.IsDigit).ToArray());
                    if (!string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, $"Số điện thoại người nhận '{dto.PatientPhone}' không liên kết với hồ sơ bệnh nhân '{dto.PatientName}' ({dto.PatientId}).", null);
                    }
                }
            }
            else
            {
                return (false, $"Không tìm thấy hồ sơ bệnh nhân với mã '{dto.PatientId}'.", null);
            }
        }

        // =====================================================================
        // RULE 3: Data Protection: Do not put full raw clinical values into email
        // =====================================================================
        bool hasPanic = dto.IsCriticalPanicValue || (dto.CriticalParameters != null && dto.CriticalParameters.Count > 0);
        var priority = hasPanic ? NotificationPriority.Critical : dto.Priority;
        bool requiresImmediate = hasPanic || dto.RequiresImmediateAction;

        string title = !string.IsNullOrWhiteSpace(dto.Title)
            ? dto.Title.Trim()
            : hasPanic
                ? $"[CẢNH BÁO NGUY HIỂM] Chỉ số xét nghiệm hoảng loạn - Đơn {dto.TestOrderId}"
                : $"Kết quả xét nghiệm đơn {dto.TestOrderId} đã hoàn tất";

        string message = !string.IsNullOrWhiteSpace(dto.Message)
            ? dto.Message.Trim()
            : "Kết quả xét nghiệm của bạn đã sẵn sàng. Vui lòng đăng nhập BMS để xem kết quả.";

        var deliveryStatus = NotificationDeliveryStatus.Delivered;
        string? failureReason = null;

        // Ensure walk-in patients have a live Patient Portal account before sending result email
        string emailMessage = message;
        if (!string.IsNullOrWhiteSpace(dto.PatientEmail) &&
            (dto.Channel == TestResultNotificationChannel.Email || dto.Channel == TestResultNotificationChannel.All) &&
            _db != null && _passwordHasher != null)
        {
            try
            {
                var patientsCol = _db.GetCollection<Patient>("patients");
                var accountsCol = _db.GetCollection<User>("accounts");
                var targetPat = await patientsCol.Find(
                    Builders<Patient>.Filter.Or(
                        Builders<Patient>.Filter.Eq(p => p.Id, dto.PatientId.Trim()),
                        Builders<Patient>.Filter.Eq(p => p.PatientCodeField, dto.PatientId.Trim())))
                    .FirstOrDefaultAsync(ct);

                if (targetPat != null)
                {
                    User? existingAcc = null;
                    if (!string.IsNullOrWhiteSpace(targetPat.AccountId))
                    {
                        existingAcc = await accountsCol.Find(Builders<User>.Filter.Eq(u => u.Id, targetPat.AccountId)).FirstOrDefaultAsync(ct);
                    }

                    if (existingAcc == null)
                    {
                        var normMail = dto.PatientEmail.Trim().ToLowerInvariant();
                        var emailTaken = await accountsCol.Find(
                            Builders<User>.Filter.Regex(u => u.Email, new MongoDB.Bson.BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(normMail)}$", "i")))
                            .AnyAsync(ct);

                        var accId = !string.IsNullOrWhiteSpace(targetPat.AccountId) && MongoDB.Bson.ObjectId.TryParse(targetPat.AccountId, out _)
                            ? targetPat.AccountId
                            : MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                        var rawPhone = targetPat.PhoneNumber ?? dto.PatientPhone ?? "000000";
                        var digits = new string(rawPhone.Where(char.IsDigit).ToArray());
                        var suffix = digits.Length >= 6 ? digits[^6..] : digits.PadLeft(6, '0');
                        var tempPass = $"Bms@{suffix}";
                        var username = targetPat.Id.ToLowerInvariant();

                        var createdAcc = new User
                        {
                            Id = accId,
                            Username = username,
                            Email = emailTaken ? $"{username}@guest.bms.local" : normMail,
                            PhoneNumber = targetPat.PhoneNumber ?? dto.PatientPhone,
                            FullName = targetPat.FullName,
                            PatientCode = targetPat.Id,
                            IdentityCardNumber = targetPat.IdentityCardNumber,
                            DateOfBirth = targetPat.DateOfBirth,
                            Gender = targetPat.GenderString,
                            Address = targetPat.Address,
                            BloodType = targetPat.BloodType,
                            RoleId = (int)UserRole.Patient,
                            Status = (int)UserStatus.Active,
                            IsActive = true,
                            MedicalProfileCompleted = true,
                            PasswordHash = _passwordHasher.Hash(tempPass),
                            CreatedAt = DateTime.UtcNow
                        };
                        await accountsCol.InsertOneAsync(createdAcc, cancellationToken: ct);
                        if (!string.Equals(targetPat.AccountId, accId, StringComparison.Ordinal))
                        {
                            await patientsCol.UpdateOneAsync(
                                Builders<Patient>.Filter.Eq(p => p.Id, targetPat.Id),
                                Builders<Patient>.Update.Set(p => p.AccountId, accId),
                                cancellationToken: ct);
                        }

                        emailMessage = $"{message} | Tài khoản tra cứu Cổng Bệnh Nhân đã được cấp tự động — Tên đăng nhập: {username} (hoặc {dto.PatientEmail.Trim()}) · Mật khẩu tạm thời: {tempPass} (vui lòng đổi mật khẩu sau khi đăng nhập).";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Notification Service] Could not auto-provision portal account for {PatientId}", dto.PatientId);
            }
        }

        // Dispatches email when channel includes Email
        if (!string.IsNullOrWhiteSpace(dto.PatientEmail) &&
            (dto.Channel == TestResultNotificationChannel.Email || dto.Channel == TestResultNotificationChannel.All))
        {
            try
            {
                bool emailSent = await _emailService.SendTestResultNotificationEmailAsync(
                    dto.PatientEmail.Trim(),
                    dto.PatientName.Trim(),
                    dto.TestOrderId.Trim(),
                    dto.TestPackageName,
                    title,
                    emailMessage,
                    hasPanic,
                    dto.CriticalParameters,
                    dto.ClinicalRecommendation,
                    ct);

                if (!emailSent)
                {
                    deliveryStatus = NotificationDeliveryStatus.Failed;
                    failureReason = "Không thể gửi email tới hộp thư người nhận qua máy chủ SMTP.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Notification Service] SMTP error dispatching email to {Email}", dto.PatientEmail);
                deliveryStatus = NotificationDeliveryStatus.Failed;
                failureReason = ex.Message;
            }
        }

        var notification = new TestResultNotification
        {
            NotificationId = $"NOTIF-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            TestOrderId = dto.TestOrderId.Trim(),
            SpecimenBarcode = dto.SpecimenBarcode?.Trim(),
            PatientId = dto.PatientId.Trim(),
            PatientName = dto.PatientName.Trim(),
            PatientPhone = dto.PatientPhone?.Trim(),
            PatientEmail = dto.PatientEmail?.Trim(),
            Title = title,
            Message = message,
            Channel = dto.Channel,
            Priority = priority,
            Status = deliveryStatus,
            FailureReason = failureReason,
            IsCriticalPanicValue = hasPanic,
            CriticalParameters = dto.CriticalParameters ?? [],
            RequiresImmediateAction = requiresImmediate,
            ClinicalRecommendation = dto.ClinicalRecommendation?.Trim(),
            TestPackageName = dto.TestPackageName?.Trim(),
            SentAt = DateTime.UtcNow,
            DeliveredAt = deliveryStatus == NotificationDeliveryStatus.Delivered ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = !string.IsNullOrWhiteSpace(operatorName) ? operatorName : "Hệ thống tự động"
        };

        var saved = await _repository.AddAsync(notification, ct);

        // =====================================================================
        // RULE 4: ISO 27789 Audit Trail for Notification Dispatch
        // =====================================================================
        if (_db != null)
        {
            try
            {
                var auditCol = _db.GetCollection<AuditLog>("audit_logs");
                await auditCol.InsertOneAsync(new AuditLog
                {
                    AuditId = $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = DateTime.UtcNow,
                    UserId = operatorId,
                    UserName = operatorName,
                    UserRole = RoleNames.Lab,
                    Action = "NOTIFY_TEST_RESULT",
                    Category = "COMMUNICATION",
                    Severity = deliveryStatus == NotificationDeliveryStatus.Failed ? "WARNING" : "INFO",
                    Status = deliveryStatus == NotificationDeliveryStatus.Failed ? "FAILED" : "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "TestResultNotification",
                    EntityId = saved.NotificationId,
                    Details = $"Gửi thông báo kết quả xét nghiệm {saved.TestOrderId} tới {saved.PatientEmail ?? saved.PatientPhone} qua kênh {saved.Channel}. Trạng thái: {saved.Status}." + (failureReason != null ? $" Lỗi: {failureReason}" : "")
                }, cancellationToken: ct);
            }
            catch { }
        }

        if (deliveryStatus == NotificationDeliveryStatus.Failed)
        {
            return (false, "Gửi thông báo thất bại: " + failureReason, MapToDetailDto(saved));
        }

        _logger.LogInformation(
            "[Notification Service] Dispatched {Channel} notification {NotifId} for order {OrderId} (Status: {Status}, Panic: {Panic})",
            notification.Channel, notification.NotificationId, notification.TestOrderId, notification.Status, notification.IsCriticalPanicValue);

        return (true, "Phát thông báo kết quả xét nghiệm qua email thành công.", MapToDetailDto(saved));
    }

    public async Task<TestResultNotificationDetailDto?> GetNotificationByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        return entity != null ? MapToDetailDto(entity) : null;
    }

    public async Task<PagedResult<TestResultNotificationListItemDto>> GetNotificationsPagedAsync(
        NotificationQueryParameters query,
        CancellationToken ct = default)
    {
        var (items, total) = await _repository.GetPagedAsync(
            query.PatientId,
            query.TestOrderId,
            query.Channel,
            query.Priority,
            query.Status,
            query.IsCritical,
            query.FromDate,
            query.ToDate,
            query.PageNumber,
            query.PageSize,
            ct);

        var dtos = items.Select(x => new TestResultNotificationListItemDto
        {
            Id = x.Id,
            NotificationId = x.NotificationId,
            TestOrderId = x.TestOrderId,
            PatientId = x.PatientId,
            PatientName = x.PatientName,
            PatientPhone = x.PatientPhone,
            Title = x.Title,
            Channel = x.Channel.ToString(),
            Priority = x.Priority.ToString(),
            Status = x.Status.ToString(),
            IsCriticalPanicValue = x.IsCriticalPanicValue,
            TestPackageName = x.TestPackageName,
            SentAt = x.SentAt?.ToString("o"),
            IsRead = x.Status == NotificationDeliveryStatus.Read
        }).ToList();

        return new PagedResult<TestResultNotificationListItemDto>
        {
            Items = dtos,
            TotalCount = total,
            CurrentPage = query.PageNumber,
            PageSize = query.PageSize
        };
    }

    public async Task<List<TestResultNotificationDetailDto>> GetPatientNotificationsAsync(
        string patientId,
        CancellationToken ct = default)
    {
        var items = await _repository.GetByPatientIdAsync(patientId, ct);
        return items.Select(MapToDetailDto).ToList();
    }

    public async Task<bool> MarkAsReadAsync(string id, CancellationToken ct = default)
    {
        return await _repository.MarkAsReadAsync(id, DateTime.UtcNow, ct);
    }

    public async Task<(bool Success, string Message, TestResultNotificationDetailDto? Data)> ResendNotificationAsync(
        string id,
        ResendNotificationDto dto,
        string operatorName,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing == null)
        {
            return (false, $"Không tìm thấy thông báo kết quả với mã '{id}'.", null);
        }

        if (dto.ChannelOverride.HasValue)
        {
            existing.Channel = dto.ChannelOverride.Value;
        }

        if (!string.IsNullOrWhiteSpace(dto.DestinationOverride))
        {
            if (existing.Channel == TestResultNotificationChannel.Sms)
                existing.PatientPhone = dto.DestinationOverride.Trim();
            else if (existing.Channel == TestResultNotificationChannel.Email)
                existing.PatientEmail = dto.DestinationOverride.Trim();
        }

        existing.RetryCount += 1;
        existing.SentAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(existing.PatientEmail) &&
            (existing.Channel == TestResultNotificationChannel.Email || existing.Channel == TestResultNotificationChannel.All))
        {
            try
            {
                bool emailSent = await _emailService.SendTestResultNotificationEmailAsync(
                    existing.PatientEmail.Trim(),
                    existing.PatientName.Trim(),
                    existing.TestOrderId.Trim(),
                    existing.TestPackageName,
                    existing.Title,
                    existing.Message,
                    existing.IsCriticalPanicValue,
                    existing.CriticalParameters,
                    existing.ClinicalRecommendation,
                    ct);

                if (emailSent)
                {
                    existing.Status = NotificationDeliveryStatus.Delivered;
                    existing.DeliveredAt = DateTime.UtcNow;
                    existing.FailureReason = null;
                }
                else
                {
                    existing.Status = NotificationDeliveryStatus.Failed;
                    existing.FailureReason = "Gửi lại email thất bại qua máy chủ SMTP.";
                }
            }
            catch (Exception ex)
            {
                existing.Status = NotificationDeliveryStatus.Failed;
                existing.FailureReason = ex.Message;
            }
        }
        else
        {
            existing.Status = NotificationDeliveryStatus.Delivered;
            existing.DeliveredAt = DateTime.UtcNow;
            existing.FailureReason = null;
        }

        _logger.LogInformation(
            "[Notification Service] Resending notification {NotifId} (Retry #{Retry}) by {Operator}",
            existing.NotificationId, existing.RetryCount, operatorName);

        var updated = await _repository.UpdateAsync(existing, ct);

        // ISO 27789 Audit Trail for Resend
        if (_db != null)
        {
            try
            {
                var auditCol = _db.GetCollection<AuditLog>("audit_logs");
                await auditCol.InsertOneAsync(new AuditLog
                {
                    AuditId = $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = DateTime.UtcNow,
                    UserId = operatorName,
                    UserName = operatorName,
                    UserRole = RoleNames.Lab,
                    Action = "RESEND_TEST_RESULT_NOTIFICATION",
                    Category = "COMMUNICATION",
                    Severity = existing.Status == NotificationDeliveryStatus.Failed ? "WARNING" : "INFO",
                    Status = existing.Status == NotificationDeliveryStatus.Failed ? "FAILED" : "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "TestResultNotification",
                    EntityId = existing.NotificationId,
                    Details = $"Gửi lại thông báo kết quả xét nghiệm {existing.TestOrderId} (Lần thử {existing.RetryCount}). Trạng thái: {existing.Status}."
                }, cancellationToken: ct);
            }
            catch { }
        }

        return (true, "Gửi lại thông báo kết quả xét nghiệm thành công.", MapToDetailDto(updated));
    }

    public async Task<int> GetUnreadCountAsync(string patientId, CancellationToken ct = default)
    {
        return await _repository.GetUnreadCountAsync(patientId, ct);
    }

    public async Task<NotificationStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var dict = await _repository.GetNotificationStatsAsync(ct);
        int total = dict.GetValueOrDefault("Total", 0);
        int delivered = dict.GetValueOrDefault("Delivered", 0);
        int critical = dict.GetValueOrDefault("CriticalPanic", 0);
        int pending = dict.GetValueOrDefault("Pending", 0);
        int failed = dict.GetValueOrDefault("Failed", 0);

        double rate = total > 0 ? Math.Round(((double)delivered / total) * 100, 1) : 100.0;

        return new NotificationStatsDto
        {
            TotalNotifications = total,
            DeliveredSuccessfully = delivered,
            CriticalPanicAlerts = critical,
            PendingCount = pending,
            FailedCount = failed,
            DeliverySuccessRate = rate
        };
    }

    private static TestResultNotificationDetailDto MapToDetailDto(TestResultNotification x)
    {
        return new TestResultNotificationDetailDto
        {
            Id = x.Id,
            NotificationId = x.NotificationId,
            TestOrderId = x.TestOrderId,
            SpecimenBarcode = x.SpecimenBarcode,
            PatientId = x.PatientId,
            PatientName = x.PatientName,
            PatientPhone = x.PatientPhone,
            PatientEmail = x.PatientEmail,
            Title = x.Title,
            Message = x.Message,
            Channel = x.Channel.ToString(),
            Priority = x.Priority.ToString(),
            Status = x.Status.ToString(),
            IsCriticalPanicValue = x.IsCriticalPanicValue,
            CriticalParameters = x.CriticalParameters ?? [],
            RequiresImmediateAction = x.RequiresImmediateAction,
            ClinicalRecommendation = x.ClinicalRecommendation,
            TestPackageName = x.TestPackageName,
            SentAt = x.SentAt?.ToString("o"),
            DeliveredAt = x.DeliveredAt?.ToString("o"),
            ReadAt = x.ReadAt?.ToString("o"),
            FailureReason = x.FailureReason,
            RetryCount = x.RetryCount,
            CreatedAt = x.CreatedAt.ToString("o"),
            CreatedBy = x.CreatedBy
        };
    }
}
