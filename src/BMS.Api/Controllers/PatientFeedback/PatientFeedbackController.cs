using BMS.Api.Authorization;
using BMS.Core.Exceptions;
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
        /// Any authenticated role can view feedbacks; anonymous authors have PII masked for non-owners.
        /// </summary>
        [Authorize(Roles = RoleSets.AnyRole)]
        [Authorize(Roles = RoleNames.Manager)]
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? rating, [FromQuery] string? category, [FromQuery] string? status, [FromQuery] string? searchTerm, [FromQuery] string? servicePackageId, CancellationToken ct)
        {
            var currentAccountId = User.AccountId();
            var feedbacks = await _service.GetAllAsync(rating, category, status, searchTerm, servicePackageId, ct);
            var masked = feedbacks.Select(f =>
            {
                var isOwner = !string.IsNullOrWhiteSpace(currentAccountId) && string.Equals(f.PatientId, currentAccountId, StringComparison.OrdinalIgnoreCase);
                if (f.IsAnonymous && !isOwner)
                {
                    f.PatientName = "Bệnh nhân ẩn danh";
                    f.PatientEmail = "";
                    f.PatientPhone = null;
                }
                return f;
            }).ToList();

            return Ok(new
            {
                message = "Retrieved patient feedbacks successfully.",
                count = masked.Count,
                data = masked
            });
        }

        /// <summary>
        /// Retrieves public average star ratings and review counts per service package (no PII).
        /// </summary>
        [AllowAnonymous]
        [HttpGet("package-ratings")]
        public async Task<IActionResult> GetPackageRatings(CancellationToken ct)
        {
            var summaries = await _service.GetPackageRatingSummariesAsync(ct);
            return Ok(new
            {
                message = "Retrieved package ratings successfully.",
                count = summaries.Count,
                data = summaries
            });
        }

        /// <summary>
        /// Retrieves anonymized public feedback reviews and rating breakdown for a specific service package (no PII).
        /// </summary>
        [AllowAnonymous]
        [HttpGet("packages/{packageIdOrCode}")]
        public async Task<IActionResult> GetPublicPackageFeedback(string packageIdOrCode, CancellationToken ct)
        {
            var detail = await _service.GetPublicPackageFeedbackAsync(packageIdOrCode, ct);
            return Ok(new
            {
                message = "Retrieved public package reviews successfully.",
                data = detail
            });
        }

        /// <summary>
        /// Retrieves completed/used service package appointments eligible for feedback by the logged-in patient.
        /// </summary>
        [Authorize(Roles = RoleNames.Patient)]
        [HttpGet("my-eligible-packages")]
        public async Task<IActionResult> GetMyEligiblePackages(CancellationToken ct)
        {
            var patientId = User.AccountId();
            if (string.IsNullOrWhiteSpace(patientId)) return Unauthorized();
            var items = await _service.GetEligiblePackagesForPatientAsync(patientId, ct);
            return Ok(new
            {
                message = "Retrieved eligible packages for feedback successfully.",
                count = items.Count,
                data = items
            });
        }

        /// <summary>
        /// Retrieves statistical breakdown of feedbacks (average rating, distribution, response rate).
        /// </summary>
        [Authorize(Roles = RoleSets.AnyRole)]
        [Authorize(Roles = RoleNames.Manager)]
        [Authorize(Roles = RoleSets.AdminOrManager)]
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
        /// Checks whether the logged-in patient has at least one completed (non-refunded) order to submit feedback.
        /// </summary>
        [Authorize(Roles = RoleNames.Patient)]
        [HttpGet("eligibility")]
        public async Task<IActionResult> GetEligibility(CancellationToken ct)
        {
            var accountId = User.AccountId() ?? string.Empty;
            var eligibility = await _service.CheckEligibilityAsync(accountId, ct);
            return Ok(new
            {
                message = eligibility.Reason,
                data = eligibility
            });
        }

        /// <summary>
        /// Retrieves all feedback submitted by a specific patient.
        /// </summary>
        [Authorize(Roles = RoleSets.AdminOrManager + "," + RoleNames.Patient)]
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
        [Authorize(Roles = RoleSets.AdminOrManager + "," + RoleNames.Patient)]
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

            try
            {
                // WHY (R14): the author is the logged-in patient; a client-supplied PatientId is ignored.
                dto.PatientId = User.AccountId();
                var created = await _service.CreateAsync(dto, ct);
                return StatusCode(201, new
                {
                    message = "Feedback submitted successfully.",
                    data = created
                });
            }
            catch (DomainValidationException ex)
            {
                return BadRequest(new { message = ex.Message, code = ex.Code });
            }
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
        /// Responds to a feedback entry (submitted by Clinic Manager or Admin).
        /// </summary>
        [Authorize(Roles = RoleSets.AdminOrManager)]
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
        /// Deletes a feedback entry (by patient owner or Admin moderator).
        /// </summary>
        [Authorize(Roles = RoleNames.Admin + "," + RoleNames.Patient)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken ct)
        {
            if (!User.IsInRole(RoleNames.Admin) && !await OwnsFeedbackAsync(id, ct))
                return NotFound(new { message = $"Feedback '{id}' not found." });
            var success = await _service.DeleteAsync(id, ct);
            if (!success)
                return NotFound(new { message = $"Feedback '{id}' not found." });

            return Ok(new { message = "Feedback deleted successfully.", success });
        }

        /// <summary>
        /// Admin requests to hide/delete a feedback; requires patient owner's consent before hiding.
        /// </summary>
        [Authorize(Roles = RoleNames.Admin)]
        [HttpPost("{id}/request-delete")]
        public async Task<IActionResult> RequestAdminDelete(string id, [FromBody] AdminDeleteRequestDto? dto, CancellationToken ct)
        {
            var adminName = User.Identity?.Name ?? "Quản trị viên (Admin)";
            var updated = await _service.RequestAdminDeleteAsync(id, adminName, dto?.Reason, ct);
            if (updated == null)
                return NotFound(new { message = $"Feedback '{id}' not found." });

            return Ok(new
            {
                message = "Đã gửi yêu cầu xóa/ẩn đến bệnh nhân. Feedback chỉ được ẩn khi bệnh nhân đồng ý.",
                data = updated
            });
        }

        /// <summary>
        /// Patient author approves or rejects Admin's delete request.
        /// </summary>
        [Authorize(Roles = RoleNames.Patient)]
        [HttpPost("{id}/consent-delete")]
        public async Task<IActionResult> RespondAdminDeleteConsent(string id, [FromBody] PatientDeleteConsentDto dto, CancellationToken ct)
        {
            if (!await OwnsFeedbackAsync(id, ct))
                return NotFound(new { message = $"Feedback '{id}' not found." });

            var updated = await _service.RespondAdminDeleteConsentAsync(id, dto.Approved, ct);
            if (updated == null)
                return NotFound(new { message = $"Feedback '{id}' not found." });

            return Ok(new
            {
                message = dto.Approved
                    ? "Bạn đã đồng ý. Feedback đã được ẩn bởi admin theo sự đồng ý của bạn."
                    : "Bạn đã từ chối yêu cầu xóa của admin.",
                data = updated
            });
        }

        /// <summary>Matrix UC79 "BN (own)": a patient edits/deletes only their own feedback (same 404 otherwise).</summary>
        private async Task<bool> OwnsFeedbackAsync(string id, CancellationToken ct)
        {
            var feedback = await _service.GetByIdAsync(id, ct);
            return feedback != null && await this.MayAccessPatientAsync(feedback.PatientId ?? string.Empty, ct);
        }
    }
}
