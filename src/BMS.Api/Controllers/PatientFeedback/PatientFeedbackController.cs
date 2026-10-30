using BMS.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using BMS.Application.DTOs.PatientFeedback;
using BMS.Application.Interfaces;

namespace BMS.Api.Controllers.PatientFeedback
{
    /// <summary>
    /// Controller for managing patient feedback, ratings, and reviews.
    /// Patients can submit and manage their feedback.
    /// Clinic Managers review and reply.
    /// Doctors, Receptionists, and Admins can inspect patient feedback.
    /// </summary>
    [Route("TestOrder/feedback")]
    [ApiController]
// WHY: UC79: Patient manages own; Manager lists, analyses and responds. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
    public class PatientFeedbackController : ControllerBase
    {
        private readonly IPatientFeedbackService _service;

        public PatientFeedbackController(IPatientFeedbackService service)
        {
            _service = service;
        }

        /// <summary>
        /// Retrieves feedback entries filtered by rating, category, status, or search keywords.
        /// </summary>
        [Authorize(Roles = RoleNames.Manager)]
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? rating, [FromQuery] string? category, [FromQuery] string? status, [FromQuery] string? searchTerm, CancellationToken ct)
        {
            var feedbacks = await _service.GetAllAsync(rating, category, status, searchTerm, ct);
            return Ok(new
            {
                message = "Retrieved patient feedbacks successfully.",
                count = feedbacks.Count(),
                data = feedbacks
            });
        }

        /// <summary>
        /// Retrieves statistical breakdown of feedbacks (average rating, distribution, response rate).
        /// </summary>
        [Authorize(Roles = RoleNames.Manager)]
        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics(CancellationToken ct)
        {
            var stats = await _service.GetStatisticsAsync(ct);
            return Ok(new
            {
                message = "Retrieved feedback statistics.",
                data = stats
            });
        }

        /// <summary>
        /// Retrieves all feedback submitted by a specific patient.
        /// </summary>
        [Authorize(Roles = RoleSets.ManagerOrPatient)]
        [HttpGet("patient/{patientId}")]
        public async Task<IActionResult> GetByPatient(string patientId, CancellationToken ct)
        {
            if (!await this.MayAccessPatientAsync(patientId, ct)) return Forbid();
            var feedbacks = await _service.GetByPatientIdAsync(patientId, ct);
            return Ok(new
            {
                message = "Retrieved patient's feedback history.",
                count = feedbacks.Count(),
                data = feedbacks
            });
        }

        /// <summary>
        /// Retrieves a single feedback entry by ID.
        /// </summary>
        [Authorize(Roles = RoleSets.ManagerOrPatient)]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken ct)
        {
            var feedback = await _service.GetByIdAsync(id, ct);
            if (feedback == null || !await this.MayAccessPatientAsync(feedback.PatientId ?? string.Empty, ct))
                return NotFound(new { message = $"Feedback '{id}' not found." });

            return Ok(new
            {
                message = "Retrieved feedback detail.",
                data = feedback
            });
        }

        /// <summary>
        /// Creates a new patient feedback entry (submitted by Patient).
        /// </summary>
        [Authorize(Roles = RoleNames.Patient)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePatientFeedbackDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // WHY (R14): the author is the logged-in patient; a client-supplied PatientId is ignored.
            dto.PatientId = User.AccountId();
            var created = await _service.CreateAsync(dto, ct);
            return StatusCode(201, new
            {
                message = "Feedback submitted successfully.",
                data = created
            });
        }

        /// <summary>
        /// Updates an existing feedback (edited by Patient or status updated by Staff).
        /// </summary>
        [Authorize(Roles = RoleNames.Patient)]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdatePatientFeedbackDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });
            if (!await OwnsFeedbackAsync(id, ct))
                return NotFound(new { message = $"Feedback '{id}' not found." });

            var updated = await _service.UpdateAsync(id, dto, ct);
            if (updated == null)
                return NotFound(new { message = $"Feedback '{id}' not found." });

            return Ok(new
            {
                message = "Feedback updated successfully.",
                data = updated
            });
        }

        /// <summary>
        /// Responds to a feedback entry (submitted by Clinic Manager).
        /// </summary>
        [Authorize(Roles = RoleNames.Manager)]
        [HttpPut("{id}/respond")]
        public async Task<IActionResult> Respond(string id, [FromBody] RespondFeedbackDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var updated = await _service.RespondAsync(id, dto, ct);
            if (updated == null)
                return NotFound(new { message = $"Feedback '{id}' not found." });

            return Ok(new
            {
                message = "Response submitted successfully.",
                data = updated
            });
        }

        /// <summary>
        /// Deletes a feedback entry (by patient or admin/manager).
        /// </summary>
        [Authorize(Roles = RoleNames.Patient)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken ct)
        {
            if (!await OwnsFeedbackAsync(id, ct))
                return NotFound(new { message = $"Feedback '{id}' not found." });
            var success = await _service.DeleteAsync(id, ct);
            if (!success)
                return NotFound(new { message = $"Feedback '{id}' not found." });

            return Ok(new { message = "Feedback deleted successfully.", success });
        }

        /// <summary>Matrix UC79 "BN (own)": a patient edits/deletes only their own feedback (same 404 otherwise).</summary>
        private async Task<bool> OwnsFeedbackAsync(string id, CancellationToken ct)
        {
            var feedback = await _service.GetByIdAsync(id, ct);
            return feedback != null && await this.MayAccessPatientAsync(feedback.PatientId ?? string.Empty, ct);
        }
    }
}
