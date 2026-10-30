using BMS.Api.Authorization;
using BMS.Core.Constants;
using MongoDB.Bson;
using MongoDB.Driver;
using BMS.Infrastructure.Data;
using System.Security.Claims;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Notifications;
using BMS.Application.DTOs.Roles;
using BMS.Application.DTOs.Users;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Notifications;

/// <summary>
/// Controller managing Patient and Clinical Diagnostic Test Result Notifications (UC60 / BR-182).
/// Supports multi-channel transmission (In-App, SMS, Email, Push) and Panic Value Critical Escalation.
/// </summary>
[ApiController]
[Route("api/v1/notifications/test-results")]
[Route("api/notifications/test-results")]
[Route("api/test-result-notifications")]
[Produces("application/json")]
// WHY: UC60: send Lab (after sign-off); patient views own; completed-patient list Lab, Manager. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class TestResultNotificationsController : ControllerBase
{
    private readonly ITestResultNotificationService _notificationService;
    private readonly ILogger<TestResultNotificationsController> _logger;
    private readonly MongoDbContext? _mongoContext;

    public TestResultNotificationsController(
        ITestResultNotificationService notificationService,
        ILogger<TestResultNotificationsController> logger,
        MongoDbContext? mongoContext = null)
    {
        _notificationService = notificationService;
        _logger = logger;
        _mongoContext = mongoContext;
    }

    /// <summary>
    /// Phát hành thông báo kết quả xét nghiệm mới (UC60).
    /// Được gọi tự động khi duyệt kết quả hoặc do KTV / Bác sĩ phát hành thủ công.
    /// </summary>
    [Authorize(Roles = RoleNames.Lab)]
    [HttpPost("dispatch")]
    [ProducesResponseType(typeof(ApiResponse<TestResultNotificationDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DispatchNotification(
        [FromBody] DispatchTestResultNotificationDto dto,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.Fail("Dữ liệu thông báo không hợp lệ.", "MSG-ERR-VALIDATION"));
        }

        var operatorId = CurrentUserId ?? "SYSTEM";
        var operatorName = CurrentUserName ?? "Hệ thống xét nghiệm BMS";

        var (success, message, data) = await _notificationService.DispatchNotificationAsync(
            dto, operatorId, operatorName, ct);

        if (!success)
        {
            return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-DISPATCH-FAILED"));
        }

        return Ok(ApiResponse<TestResultNotificationDetailDto>.Ok(data!, message));
    }

    /// <summary>
    /// Truy vấn danh sách thông báo kết quả xét nghiệm có phân trang và bộ lọc (UC60).
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TestResultNotificationListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] NotificationQueryParameters query,
        CancellationToken ct = default)
    {
        var result = await _notificationService.GetNotificationsPagedAsync(query, ct);
        return Ok(ApiResponse<PagedResult<TestResultNotificationListItemDto>>.Ok(
            result,
            "Tải danh sách thông báo kết quả xét nghiệm thành công."));
    }

    /// <summary>
    /// Xem chi tiết một thông báo kết quả xét nghiệm theo ID hoặc mã thông báo (UC60).
    /// </summary>
    [Authorize(Roles = RoleSets.LabManagerOrPatient)]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<TestResultNotificationDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNotificationById(
        string id,
        CancellationToken ct = default)
    {
        var result = await _notificationService.GetNotificationByIdAsync(id, ct);
        // A patient asking for someone else's notification gets the same 404 as a missing one (anti-BOLA, R14).
        if (result != null && !await this.MayAccessPatientAsync(result.PatientId, ct)) result = null;
        if (result == null)
        {
            return NotFound(ApiResponse<object>.Fail(
                $"Không tìm thấy thông báo kết quả xét nghiệm với mã '{id}'.",
                "MSG-ERR-NOTIF-NOT-FOUND"));
        }

        return Ok(ApiResponse<TestResultNotificationDetailDto>.Ok(result, "Tải chi tiết thông báo thành công."));
    }

    /// <summary>
    /// Lấy danh sách thông báo kết quả xét nghiệm của một bệnh nhân cụ thể (UC60 / Mobile).
    /// </summary>
    [Authorize(Roles = RoleNames.Patient)]
    [HttpGet("patient/{patientId}")]
    [ProducesResponseType(typeof(ApiResponse<List<TestResultNotificationDetailDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatientNotifications(
        string patientId,
        CancellationToken ct = default)
    {
        // WHY (R14, smoke SMK-BN-08): patientId is in the URL, so ownership is checked against the token account.
        if (!await this.MayAccessPatientAsync(patientId, ct)) return Forbid();
        var list = await _notificationService.GetPatientNotificationsAsync(patientId, ct);
        return Ok(ApiResponse<List<TestResultNotificationDetailDto>>.Ok(
            list,
            $"Tải danh sách thông báo của bệnh nhân {patientId} thành công."));
    }

    /// <summary>
    /// Đếm số lượng thông báo chưa đọc của bệnh nhân (UC60 / Mobile / Web Badge).
    /// </summary>
    [Authorize(Roles = RoleNames.Patient)]
    [HttpGet("patient/{patientId}/unread-count")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatientUnreadCount(
        string patientId,
        CancellationToken ct = default)
    {
        if (!await this.MayAccessPatientAsync(patientId, ct)) return Forbid();
        var count = await _notificationService.GetUnreadCountAsync(patientId, ct);
        return Ok(ApiResponse<int>.Ok(count, "Số lượng thông báo chưa đọc."));
    }

    /// <summary>
    /// Đánh dấu một thông báo là đã đọc (UC60).
    /// </summary>
    [Authorize(Roles = RoleNames.Patient)]
    [HttpPut("{id}/read")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAsRead(
        string id,
        CancellationToken ct = default)
    {
        var owned = await _notificationService.GetNotificationByIdAsync(id, ct);
        if (owned == null || !await this.MayAccessPatientAsync(owned.PatientId, ct))
            return NotFound(ApiResponse<bool>.Fail("Không tìm thấy thông báo cần đánh dấu đã đọc.", "MSG-ERR-NOTIF-NOT-FOUND"));
        var success = await _notificationService.MarkAsReadAsync(id, ct);
        if (!success)
        {
            return NotFound(ApiResponse<bool>.Fail("Không tìm thấy thông báo cần đánh dấu đã đọc.", "MSG-ERR-NOTIF-NOT-FOUND"));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Đã đánh dấu thông báo là đã đọc."));
    }

    /// <summary>
    /// Gửi lại thông báo kết quả xét nghiệm qua SMS / Email / Push (UC60).
    /// </summary>
    [Authorize(Roles = RoleNames.Lab)]
    [HttpPost("{id}/resend")]
    [ProducesResponseType(typeof(ApiResponse<TestResultNotificationDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendNotification(
        string id,
        [FromBody] ResendNotificationDto dto,
        CancellationToken ct = default)
    {
        var operatorName = CurrentUserName ?? "Nhân viên y tế BMS";
        var (success, message, data) = await _notificationService.ResendNotificationAsync(
            id, dto, operatorName, ct);

        if (!success)
        {
            return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-RESEND-FAILED"));
        }

        return Ok(ApiResponse<TestResultNotificationDetailDto>.Ok(data!, message));
    }

    /// <summary>
    /// Lấy thống kê tình hình gửi thông báo và cảnh báo hoảng loạn toàn viện (UC60 / Web Dashboard).
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ApiResponse<NotificationStatsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotificationStats(CancellationToken ct = default)
    {
        var stats = await _notificationService.GetStatsAsync(ct);
        return Ok(ApiResponse<NotificationStatsDto>.Ok(stats, "Thống kê thông báo kết quả xét nghiệm."));
    }

    private string? CurrentUserId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    private string? CurrentUserName =>
        User.FindFirstValue("name") ?? User.FindFirstValue("fullName") ?? User.FindFirstValue(ClaimTypes.Name);

    /// <summary>
    /// Lấy danh sách bệnh nhân và đơn xét nghiệm đã hoàn thành để chọn phát hành thông báo (UC60).
    /// Truy vấn trực tiếp từ MongoDB blood_samples, test_results và patients.
    /// </summary>
    [Authorize(Roles = RoleSets.LabOrManager)]
    [HttpGet("completed-patients")]
    [HttpGet("completed-tests")]
    [ProducesResponseType(typeof(ApiResponse<List<CompletedTestPatientDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCompletedPatients(CancellationToken ct = default)
    {
        var result = new List<CompletedTestPatientDto>();
        var db = _mongoContext?.Database;

        if (db != null)
        {
            try
            {
                var samplesCol = db.GetCollection<BsonDocument>("blood_samples");
                var resultsCol = db.GetCollection<BsonDocument>("test_results");
                var patientsCol = db.GetCollection<BsonDocument>("patients");
                var packagesCol = db.GetCollection<BsonDocument>("test_packages");

                // 1. Completed blood samples
                var completedFilter = Builders<BsonDocument>.Filter.In("status", new[] { "Completed", "Approved" });
                var completedSamples = await samplesCol.Find(completedFilter).ToListAsync(ct);

                // 2. Approved / Completed test results
                var approvedResults = await resultsCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);

                // Pre-cache packages
                var allPackages = await packagesCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
                var packageMap = allPackages.ToDictionary(
                    p => p.Contains("_id") ? p["_id"].AsString : "",
                    p => p.Contains("package_name") ? p["package_name"].AsString : (p.Contains("name") ? p["name"].AsString : "Xét nghiệm lâm sàng")
                );

                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var s in completedSamples)
                {
                    var barcode = s.Contains("barcode") ? s["barcode"].AsString : (s.Contains("_id") ? s["_id"].AsString : "");
                    var patientId = s.Contains("patient_id") ? s["patient_id"].AsString : "";
                    var orderId = s.Contains("appointment_id") ? s["appointment_id"].AsString : (s.Contains("test_order_id") ? s["test_order_id"].AsString : barcode);

                    var key = $"{patientId}_{barcode}_{orderId}";
                    if (!seenKeys.Add(key)) continue;

                    string patientName = s.Contains("patient_name") && !s["patient_name"].IsBsonNull ? s["patient_name"].AsString : "";
                    string phone = "";
                    string email = "";

                    if (!string.IsNullOrWhiteSpace(patientId))
                    {
                        var pDoc = await patientsCol.Find(Builders<BsonDocument>.Filter.Or(
                            Builders<BsonDocument>.Filter.Eq("_id", patientId),
                            Builders<BsonDocument>.Filter.Eq("patient_code", patientId)
                        )).FirstOrDefaultAsync(ct);

                        if (pDoc != null)
                        {
                            if (string.IsNullOrWhiteSpace(patientName))
                            {
                                patientName = pDoc.Contains("fullname") ? pDoc["fullname"].AsString : (pDoc.Contains("full_name") ? pDoc["full_name"].AsString : "");
                            }
                            phone = pDoc.Contains("phone") ? pDoc["phone"].AsString : (pDoc.Contains("phone_number") ? pDoc["phone_number"].AsString : "");
                            email = pDoc.Contains("email") && !pDoc["email"].IsBsonNull ? pDoc["email"].AsString : "";
                        }
                    }

                    string pkgName = s.Contains("test_package_name") && !s["test_package_name"].IsBsonNull && !string.IsNullOrWhiteSpace(s["test_package_name"].AsString)
                        ? s["test_package_name"].AsString
                        : "Xét nghiệm sinh hóa / huyết học";

                    // Check if there is a matching test_result for conclusion or panic alert
                    var matchingRes = approvedResults.FirstOrDefault(r =>
                        (r.Contains("sample_id") && r["sample_id"].AsString.Equals(s["_id"].AsString, StringComparison.OrdinalIgnoreCase)) ||
                        (r.Contains("patient_id") && r["patient_id"].AsString.Equals(patientId, StringComparison.OrdinalIgnoreCase)));

                    string conclusion = "";
                    bool isCritical = false;

                    if (matchingRes != null)
                    {
                        if (matchingRes.Contains("conclusion_notes") && !matchingRes["conclusion_notes"].IsBsonNull)
                        {
                            conclusion = matchingRes["conclusion_notes"].AsString;
                            if (conclusion.Contains("CẢNH BÁO", StringComparison.OrdinalIgnoreCase) ||
                                conclusion.Contains("nguy hiểm", StringComparison.OrdinalIgnoreCase) ||
                                conclusion.Contains("nguy kịch", StringComparison.OrdinalIgnoreCase))
                            {
                                isCritical = true;
                            }
                        }

                        if (matchingRes.Contains("package_id") && packageMap.TryGetValue(matchingRes["package_id"].AsString, out var foundPkg))
                        {
                            pkgName = foundPkg;
                        }
                    }

                    DateTime? compDate = null;
                    if (s.Contains("completed_at") && s["completed_at"].IsBsonDateTime)
                    {
                        compDate = s["completed_at"].ToUniversalTime();
                    }

                    result.Add(new CompletedTestPatientDto
                    {
                        PatientId = patientId,
                        PatientName = !string.IsNullOrWhiteSpace(patientName) ? patientName : "Bệnh nhân " + patientId,
                        PatientPhone = phone,
                        PatientEmail = email,
                        TestOrderId = orderId,
                        SpecimenBarcode = barcode,
                        TestPackageName = pkgName,
                        ConclusionNotes = conclusion,
                        IsCritical = isCritical,
                        CompletedAt = compDate
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi truy vấn danh sách bệnh nhân hoàn thành xét nghiệm từ MongoDB.");
            }
        }

        return Ok(ApiResponse<List<CompletedTestPatientDto>>.Ok(result, "Tải danh sách bệnh nhân hoàn thành xét nghiệm thành công."));
    }

}