using BMS.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Notifications;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Api.Controllers.Notifications;

/// <summary>
/// Controller providing multi-channel push notification dispatch, device token registration, and audit logs (UC58 / Send Push Notifications).
/// Conforms to: Production Engineering Standards & AGENTS-DESIGN.md.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Route("api/notifications")]
// WHY: UC58: dispatch Lab, Manager, Receptionist; tokens/inbox own; logs Admin, Manager. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class PushNotificationsController : ControllerBase
{
    private readonly IPushNotificationService _notificationService;
    private readonly IMongoDatabase? _database;

    public PushNotificationsController(IPushNotificationService notificationService, IMongoDatabase? database = null)
    {
        _notificationService = notificationService;
        _database = database;
    }

    private async Task<bool> IsTargetAdminAccountAsync(string? recipientUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recipientUserId) || _database == null) return false;
        var trimmed = recipientUserId.Trim();
        if (trimmed.Equals("ADMIN", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("ROLE_ADMIN", StringComparison.OrdinalIgnoreCase))
            return true;

        var accounts = _database.GetCollection<BsonDocument>("accounts");
        var fb = Builders<BsonDocument>.Filter;
        var idFilters = new List<FilterDefinition<BsonDocument>>
        {
            fb.Eq("_id", trimmed),
            fb.Eq("username", trimmed),
            fb.Eq("Username", trimmed)
        };
        if (ObjectId.TryParse(trimmed, out var oid))
            idFilters.Add(fb.Eq("_id", oid));

        var doc = await accounts.Find(fb.Or(idFilters)).FirstOrDefaultAsync(ct);
        if (doc == null) return false;
        foreach (var k in new[] { "roleId", "RoleId", "role_id" })
        {
            if (doc.TryGetValue(k, out var val) && !val.IsBsonNull)
            {
                if (val.IsInt32 && val.AsInt32 == 1) return true;
                if (val.IsInt64 && val.AsInt64 == 1) return true;
                if (int.TryParse(val.ToString(), out var parsed) && parsed == 1) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Dispatches a push notification to a recipient user across channels (UC58).
    /// </summary>
    [Authorize(Roles = RoleSets.NotificationDispatchers + "," + RoleNames.Admin)]
    [HttpPost("dispatch")]
    public async Task<IActionResult> DispatchNotification([FromBody] DispatchNotificationRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return BadRequest(new { success = false, message = "Request payload cannot be null." });
        }

        if (!User.IsInRole(RoleNames.Admin) && await IsTargetAdminAccountAsync(request.RecipientUserId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                success = false,
                message = "Quản lý và các vai trò khác không có quyền gửi hoặc thao tác thông báo trực tiếp lên tài khoản Quản trị viên hệ thống (Admin)."
            });
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
    [Authorize(Roles = RoleSets.AnyRole)]
    [HttpPost("register-token")]
    public async Task<IActionResult> RegisterDeviceToken([FromBody] RegisterDeviceTokenRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.DeviceToken))
        {
            return BadRequest(new { success = false, message = "UserId and DeviceToken are required." });
        }
        // WHY (R14): a device token may only be registered for the caller's own account.
        if (!this.IsSelf(request.UserId)) return Forbid();

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
    [Authorize(Roles = RoleSets.AnyRole)]
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserNotifications(string userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
    // WHY (R14): own inbox only; userId in the URL must be the token account.
    if (!this.IsSelf(userId)) return Forbid();
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
    [Authorize(Roles = RoleSets.AnyRole)]
    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { success = false, message = "Notification ID is required." });
        }

        try
        {
            var existing = await _notificationService.GetNotificationByIdAsync(id, cancellationToken);
            if (existing == null || (!User.IsInRole(RoleNames.Admin) && !this.IsSelf(existing.RecipientUserId)))
            {
                return NotFound(new { success = false, message = $"Notification '{id}' was not found." });
            }

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
    [Authorize(Roles = RoleSets.AnyRole)]
    [HttpPatch("user/{userId}/read-all")]
    public async Task<IActionResult> MarkAllAsRead(string userId, CancellationToken cancellationToken = default)
    {
    // WHY (R14): own inbox only; userId in the URL must be the token account.
    if (!this.IsSelf(userId)) return Forbid();
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
    /// Resends an existing push notification by identifier (UC58).
    /// </summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpPost("{id}/resend")]
    public async Task<IActionResult> ResendNotification(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { success = false, message = "Notification ID is required." });

        if (!User.IsInRole(RoleNames.Admin))
        {
            var existing = await _notificationService.GetNotificationByIdAsync(id, cancellationToken);
            if (existing != null && await IsTargetAdminAccountAsync(existing.RecipientUserId, cancellationToken))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    success = false,
                    message = "Quản lý không có quyền thao tác thông báo thuộc tài khoản Quản trị viên hệ thống (Admin)."
                });
            }
        }

        var result = await _notificationService.ResendNotificationAsync(id, cancellationToken);
        if (result == null)
            return NotFound(new { success = false, message = $"Notification '{id}' was not found." });

        return Ok(new
        {
            success = result.Success,
            data = result,
            message = result.Message
        });
    }

    /// <summary>
    /// Retrieves notification audit logs with filters (Status, Channel, Priority, Date Range).
    /// </summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
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