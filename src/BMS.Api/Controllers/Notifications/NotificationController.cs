using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Testorder_service.Models.DTO;
using Testorder_service.Services.Interface;

namespace Testorder_service.Controllers
{
    /// <summary>
    /// Controller managing past and ongoing system notifications.
    /// Accessible by Patient, Doctor, and Staff roles to review appointment reminders, result readiness, and alerts.
    /// </summary>
    [Route("TestOrder/[controller]")]
    [ApiController]
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
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUser(string userId, [FromQuery] string? role)
        {
            var data = await _service.GetNotificationsForUserAsync(userId, role);
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
        [HttpGet("role/{role}")]
        public async Task<IActionResult> GetByRole(string role)
        {
            var data = await _service.GetNotificationsByRoleAsync(role);
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
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(string id)
        {
            var success = await _service.MarkAsReadAsync(id);
            if (!success)
                return NotFound(new { message = $"Notification '{id}' was not found." });

            return Ok(new { message = "Notification marked as read." });
        }

        /// <summary>
        /// Marks all notifications for a specific user as read.
        /// </summary>
        /// <param name="userId">Recipient user identifier.</param>
        /// <returns>HTTP status confirming update.</returns>
        [HttpPut("user/{userId}/read-all")]
        public async Task<IActionResult> MarkAllAsRead(string userId)
        {
            var success = await _service.MarkAllAsReadAsync(userId);
            return Ok(new { message = "All notifications marked as read.", success });
        }

        /// <summary>
        /// Dispatches a new notification into the history repository.
        /// </summary>
        /// <param name="dto">Notification creation payload.</param>
        /// <returns>Created notification record.</returns>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateNotificationDto dto)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _service.CreateNotificationAsync(dto);
            return Ok(new
            {
                message = "Notification created successfully.",
                data = created
            });
        }
    }
}
