using BMS.Application.DTOs.Appointment;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Api.Controllers.Clinic;

/// <summary>
/// UC22 – Notify Appointment & Confirmation Receipt API.
/// Provides appointment booking confirmation details, check-in QR codes, fasting instructions,
/// and automated / on-demand notification dispatches (Email, Push, In-App).
/// </summary>
[ApiController]
[Route("api")]
public class AppointmentNotificationsController : ControllerBase
{
    private readonly IAppointmentNotificationService _notificationService;

    public AppointmentNotificationsController(IAppointmentNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>
    /// UC22 - Retrieves appointment confirmation receipt and check-in QR payload.
    /// Can be fetched by Appointment ID or AppointmentCode (e.g. APT-261010-0342).
    /// </summary>
    [HttpGet("appointments/{id}/confirmation")]
    [ProducesResponseType(typeof(AppointmentConfirmationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfirmation(string id, CancellationToken ct)
    {
        var result = await _notificationService.GetConfirmationAsync(id, ct);
        if (result == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy thông tin xác nhận cho cuộc hẹn này."
            });
        }

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// UC22 - Triggers or resends notification (Email, Push, In-App) for an appointment.
    /// </summary>
    [HttpPost("appointments/{id}/notify")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendNotification(
        string id,
        [FromBody] SendNotificationRequestDto request,
        CancellationToken ct)
    {
        var success = await _notificationService.SendNotificationAsync(id, request, ct);
        if (!success)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy lịch hẹn để gửi thông báo."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Đã gửi thông báo xác nhận cuộc hẹn thành công."
        });
    }

    /// <summary>
    /// UC22 - Gets appointment notifications for a patient.
    /// </summary>
    [HttpGet("notifications/appointments")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatientNotifications(
        [FromQuery] string? patientId,
        CancellationToken ct)
    {
        var authUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(authUserId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "Vui lòng đăng nhập để xem thông báo."
            });
        }

        // If user is a Patient, caller's identity cannot be overridden by query param
        string targetPatientId = User.IsInRole("Patient")
            ? authUserId
            : (!string.IsNullOrWhiteSpace(patientId) ? patientId : authUserId);

        var list = await _notificationService.GetPatientNotificationsAsync(targetPatientId, ct);
        return Ok(new
        {
            success = true,
            count = list.Count,
            data = list
        });
    }

    /// <summary>
    /// UC22 - Marks a notification as read.
    /// </summary>
    [HttpPut("notifications/{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id, CancellationToken ct)
    {
        var success = await _notificationService.MarkAsReadAsync(id, ct);
        return Ok(new { success });
    }
}
