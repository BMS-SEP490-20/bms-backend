using System;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Notifications;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Notifications;

/// <summary>
/// Controller providing multi-channel push notification dispatch, device token registration, and audit logs (UC58 / Send Push Notifications).
/// Conforms to: Production Engineering Standards & AGENTS-DESIGN.md.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Route("api/notifications")]
public class PushNotificationsController : ControllerBase
{
    private readonly IPushNotificationService _notificationService;

    public PushNotificationsController(IPushNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>
    /// Dispatches a push notification to a recipient user across channels (UC58).
    /// </summary>
    [HttpPost("dispatch")]
    public async Task<IActionResult> DispatchNotification([FromBody] DispatchNotificationRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return BadRequest(new { success = false, message = "Request payload cannot be null." });
        }

        try
        {
            var result = await _notificationService.DispatchNotificationAsync(request, cancellationToken);
            if (!result.Success)
            {
                return StatusCode(429, new
                {
                    success = false,
                    data = result,
                    message = result.Message
                });
            }

            return Ok(new
            {
                success = true,
                data = result,
                message = "Notification dispatched successfully."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while dispatching notification: " + ex.Message
            });
        }
    }

    /// <summary>
    /// Registers or updates an FCM / Web Push device token for a user.
    /// </summary>
    [HttpPost("register-token")]
    public async Task<IActionResult> RegisterDeviceToken([FromBody] RegisterDeviceTokenRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.DeviceToken))
        {
            return BadRequest(new { success = false, message = "UserId and DeviceToken are required." });
        }

        try
        {
            var success = await _notificationService.RegisterDeviceTokenAsync(request, cancellationToken);
            return Ok(new
            {
                success,
                message = success ? "Device token registered successfully." : "Failed to register device token."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while registering token: " + ex.Message
            });
        }
    }

    /// <summary>
    /// Retrieves user notifications with pagination.
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserNotifications(string userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { success = false, message = "UserId is required." });
        }

        try
        {
            var result = await _notificationService.GetUserNotificationsAsync(userId, page, pageSize, cancellationToken);
            return Ok(new
            {
                success = true,
                data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while fetching user notifications: " + ex.Message
            });
        }
    }

    /// <summary>
    /// Marks a specific notification as READ.
    /// </summary>
    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { success = false, message = "Notification ID is required." });
        }

        try
        {
            var success = await _notificationService.MarkAsReadAsync(id, cancellationToken);
            if (!success)
            {
                return NotFound(new { success = false, message = $"Notification '{id}' was not found." });
            }

            return Ok(new { success = true, message = "Notification marked as read." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while marking notification as read: " + ex.Message
            });
        }
    }

    /// <summary>
    /// Marks all unread notifications for a user as READ.
    /// </summary>
    [HttpPatch("user/{userId}/read-all")]
    public async Task<IActionResult> MarkAllAsRead(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { success = false, message = "UserId is required." });
        }

        try
        {
            var count = await _notificationService.MarkAllAsReadAsync(userId, cancellationToken);
            return Ok(new
            {
                success = true,
                count,
                message = $"Marked {count} notifications as read."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while marking notifications as read: " + ex.Message
            });
        }
    }

    /// <summary>
    /// Retrieves notification audit logs with filters (Status, Channel, Priority, Date Range).
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetAuditLogs([FromQuery] PushNotificationQueryParameters query, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _notificationService.GetAuditLogsAsync(query, cancellationToken);
            return Ok(new
            {
                success = true,
                data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while fetching audit logs: " + ex.Message
            });
        }
    }
}
