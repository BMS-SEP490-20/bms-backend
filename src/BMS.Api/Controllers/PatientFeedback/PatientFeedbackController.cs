using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Testorder_service.Models.DTO;
using Testorder_service.Services.Interface;

namespace Testorder_service.Controllers
{
    /// <summary>
    /// Controller for managing patient feedback, ratings, and reviews.
    /// Patients can submit and manage their feedback.
    /// Clinic Managers review and reply.
    /// Doctors, Receptionists, and Admins can inspect patient feedback.
    /// </summary>
    [Route("TestOrder/feedback")]
    [ApiController]
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
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? rating, [FromQuery] string? category, [FromQuery] string? status, [FromQuery] string? searchTerm)
        {
            var feedbacks = await _service.GetAllAsync(rating, category, status, searchTerm);
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
        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics()
        {
            var stats = await _service.GetStatisticsAsync();
            return Ok(new
            {
                message = "Retrieved feedback statistics.",
                data = stats
            });
        }

        /// <summary>
        /// Retrieves all feedback submitted by a specific patient.
        /// </summary>
        [HttpGet("patient/{patientId}")]
        public async Task<IActionResult> GetByPatient(string patientId)
        {
            var feedbacks = await _service.GetByPatientIdAsync(patientId);
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
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var feedback = await _service.GetByIdAsync(id);
            if (feedback == null)
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
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePatientFeedbackDto dto)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _service.CreateAsync(dto);
            return StatusCode(201, new
            {
                message = "Feedback submitted successfully.",
                data = created
            });
        }

        /// <summary>
        /// Updates an existing feedback (edited by Patient or status updated by Staff).
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdatePatientFeedbackDto dto)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            var updated = await _service.UpdateAsync(id, dto);
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
        [HttpPut("{id}/respond")]
        public async Task<IActionResult> Respond(string id, [FromBody] RespondFeedbackDto dto)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var updated = await _service.RespondAsync(id, dto);
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
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var success = await _service.DeleteAsync(id);
            if (!success)
                return NotFound(new { message = $"Feedback '{id}' not found." });

            return Ok(new { message = "Feedback deleted successfully.", success });
        }
    }
}
