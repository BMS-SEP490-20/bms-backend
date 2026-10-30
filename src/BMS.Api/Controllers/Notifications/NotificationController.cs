using BMS.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using BMS.Application.DTOs.Notifications;
using BMS.Application.Interfaces;

namespace BMS.Api.Controllers.Notifications
{
    /// <summary>
    /// Controller managing past and ongoing system notifications.
    /// Accessible by Patient, Doctor, and Staff roles to review appointment reminders, result readiness, and alerts.
    /// </summary>
    [Route("TestOrder/[controller]")]
    [ApiController]
// WHY: UC59: every role reads own; create is system-side (Admin until a server writer exists). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _service;

        public NotificationController(INotificationService service)
        {
            _service = service;
        }

        /// <summary>
        /// Retrieves past notification history for a specific recipient user.
        /// </summary>
        /// <param name="userId">Unique recipient user identifier.</param>
        /// <param name="role">Optional user role for seed/filter context.</param>
        /// <returns>List of notification records.</returns>
        [Authorize(Roles = RoleSets.AnyRole)]
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUser(string userId, [FromQuery] string? role, CancellationToken ct)
        {
    // WHY (R14): own inbox only; userId in the URL must be the token account.
    if (!this.IsSelf(userId)) return Forbid();
            var data = await _service.GetNotificationsForUserAsync(userId, role, ct);
            return Ok(new
            {
                message = "Retrieved notifications history successfully.",
                count = data.Count(),
                data = data
            });
        }

        /// <summary>
        /// Retrieves notifications filtered by audience role (Patient, Doctor, Staff).
        /// </summary>
        /// <param name="role">Target role identifier.</param>
        /// <returns>List of notifications.</returns>
        [Authorize(Roles = RoleSets.AnyRole)]
        [HttpGet("role/{role}")]
        public async Task<IActionResult> GetByRole(string role, CancellationToken ct)
        {
    // WHY: a caller may only read the audience feed of their own role.
    if (!User.IsInRole(BMS.Core.Constants.RoleNames.Normalize(role) ?? string.Empty)) return Forbid();
            var data = await _service.GetNotificationsByRoleAsync(role, ct);
            return Ok(new
            {
                message = "Retrieved notifications by role successfully.",
                count = data.Count(),
                data = data
            });
        }

        /// <summary>
        /// Marks a specific notification as read.
        /// </summary>
        /// <param name="id">Notification identifier.</param>
        /// <returns>HTTP status confirming update.</returns>
        [Authorize(Roles = RoleSets.AnyRole)]
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(string id, CancellationToken ct)
        {
            var success = await _service.MarkAsReadAsync(id, ct);
            if (!success)
                return NotFound(new { message = $"Notification '{id}' was not found." });

            return Ok(new { message = "Notification marked as read." });
        }

        /// <summary>
        /// Marks all notifications for a specific user as read.
        /// </summary>
        /// <param name="userId">Recipient user identifier.</param>
        /// <returns>HTTP status confirming update.</returns>
        [Authorize(Roles = RoleSets.AnyRole)]
        [HttpPut("user/{userId}/read-all")]
        public async Task<IActionResult> MarkAllAsRead(string userId, CancellationToken ct)
        {
    // WHY (R14): own inbox only; userId in the URL must be the token account.
    if (!this.IsSelf(userId)) return Forbid();
            var success = await _service.MarkAllAsReadAsync(userId, ct);
            return Ok(new { message = "All notifications marked as read.", success });
        }

        /// <summary>
        /// Dispatches a new notification into the history repository.
        /// </summary>
        /// <param name="dto">Notification creation payload.</param>
        /// <returns>Created notification record.</returns>
        [Authorize(Roles = RoleNames.Admin)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateNotificationDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _service.CreateNotificationAsync(dto, ct);
            return Ok(new
            {
                message = "Notification created successfully.",
                data = created
            });
        }
    }
}
