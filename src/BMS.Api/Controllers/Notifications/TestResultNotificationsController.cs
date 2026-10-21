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
[Authorize]
public class TestResultNotificationsController : ControllerBase
{
    private readonly ITestResultNotificationService _notificationService;
    private readonly ILogger<TestResultNotificationsController> _logger;

    public TestResultNotificationsController(
        ITestResultNotificationService notificationService,
        ILogger<TestResultNotificationsController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Phát hành thông báo kết quả xét nghiệm mới (UC60).
    /// Được gọi tự động khi duyệt kết quả hoặc do KTV / Bác sĩ phát hành thủ công.
    /// </summary>
    [HttpPost("dispatch")]
    [Authorize(Roles = "Admin,Manager,LabHead,LabTech,Doctor")]
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
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<TestResultNotificationDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNotificationById(
        string id,
        CancellationToken ct = default)
    {
        var result = await _notificationService.GetNotificationByIdAsync(id, ct);
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
    [HttpGet("patient/{patientId}")]
    [ProducesResponseType(typeof(ApiResponse<List<TestResultNotificationDetailDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatientNotifications(
        string patientId,
        CancellationToken ct = default)
    {
        var list = await _notificationService.GetPatientNotificationsAsync(patientId, ct);
        return Ok(ApiResponse<List<TestResultNotificationDetailDto>>.Ok(
            list,
            $"Tải danh sách thông báo của bệnh nhân {patientId} thành công."));
    }

    /// <summary>
    /// Đếm số lượng thông báo chưa đọc của bệnh nhân (UC60 / Mobile / Web Badge).
    /// </summary>
    [HttpGet("patient/{patientId}/unread-count")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatientUnreadCount(
        string patientId,
        CancellationToken ct = default)
    {
        var count = await _notificationService.GetUnreadCountAsync(patientId, ct);
        return Ok(ApiResponse<int>.Ok(count, "Số lượng thông báo chưa đọc."));
    }

    /// <summary>
    /// Đánh dấu một thông báo là đã đọc (UC60).
    /// </summary>
    [HttpPut("{id}/read")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAsRead(
        string id,
        CancellationToken ct = default)
    {
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
    [HttpPost("{id}/resend")]
    [Authorize(Roles = "Admin,Manager,LabHead,LabTech,Doctor")]
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
    [HttpGet("stats")]
    [Authorize(Roles = "Admin,Manager,LabHead,LabTech,Doctor")]
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
}
