using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Mvc;
using BMS.Application.DTOs.Appointment;
using BMS.Application.Interfaces;
using System.Linq;

namespace BMS.Api.Controllers.Testorder
{
    /// <summary>
    /// Legacy-route appointment API (<c>TestOrder/Appointment</c>) used by Scan QR Check-in (#28) and the web
    /// sample screens. Delegates to the same <see cref="IAppointmentService"/> as <c>api/appointments</c>.
    /// </summary>
    [Route("TestOrder/[controller]")]
    [ApiController]
// WHY: UC24 legacy: Receptionist, Manager. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.ReceptionOrManager)]
    public class AppointmentController : ControllerBase
    {
        private readonly IAppointmentService _service;

        /// <summary>Creates the controller over the appointment service.</summary>
        public AppointmentController(IAppointmentService service)
        {
            _service = service;
        }

        /// <summary>Lists appointments with paging.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            // TODO(senior-dev, Đợt 2): page in MongoDB via GetFilteredAsync instead of in memory.
            var data = await _service.GetAllAsync(ct);
            var total = data.Count();
            var items = data.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            if (!items.Any())
                return NotFound(new { message = "Không có cuộc hẹn nào được tìm thấy." });

            return Ok(new
            {
                message = "Lấy danh sách cuộc hẹn thành công.",
                page,
                pageSize,
                total,
                data = items
            });
        }

        /// <summary>
        /// Retrieves an appointment by its unique identifier.
        /// Used by the Scan QR Check-in workflow to verify appointment validity and details before check-in.
        /// </summary>
        /// <param name="id">The unique appointment ID or QR-scanned code.</param>
        /// <returns>An <see cref="IActionResult"/> containing the appointment details if found; otherwise, 404 NotFound.</returns>
        /// <response code="200">Returns the appointment details.</response>
        /// <response code="400">If the provided ID is null or whitespace.</response>
        /// <response code="404">If no appointment with the given ID exists.</response>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Appointment ID is required.");

            var data = await _service.GetDetailByIdAsync(id, ct);

            if (data == null)
                return NotFound($"Appointment with ID '{id}' not found.");

            return Ok(data);
        }

        /// <summary>
        /// Creates a new test appointment.
        /// </summary>
        /// <param name="dto">The appointment creation payload.</param>
        /// <returns>The newly created appointment resource.</returns>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AppointmentCreateDto dto, CancellationToken ct)
        {
            var result = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.AppointmentId }, result);
        }

        /// <summary>
        /// Updates existing appointment details or sets its status to 'checked_in' during Scan QR Check-in.
        /// </summary>
        /// <param name="id">The unique identifier of the appointment to update.</param>
        /// <param name="dto">The update payload containing updated time, status, or reschedule notes.</param>
        /// <returns>204 NoContent if successful; 404 NotFound if the appointment does not exist.</returns>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] AppointmentUpdateDto dto, CancellationToken ct)
        {
            var success = await _service.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        /// <summary>
        /// Deletes an appointment from the system.
        /// </summary>
        /// <param name="id">The unique identifier of the appointment to delete.</param>
        /// <returns>204 NoContent on success; 404 NotFound if the record is missing.</returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken ct)
        {
            var success = await _service.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        /// <summary>Lists appointments of one patient with paging.</summary>
        [HttpGet("patient/{patientId}")]
        public async Task<IActionResult> GetByPatientId(
            string patientId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            CancellationToken ct = default)
        {
            var data = await _service.GetByPatientIdAsync(patientId, ct);
            if (data == null || !data.Any())
                return NotFound(new { message = "Không có cuộc hẹn nào cho bệnh nhân này." });

            var total = data.Count();
            var items = data.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new
            {
                message = "Lấy danh sách cuộc hẹn của bệnh nhân thành công.",
                page,
                pageSize,
                total,
                data = items
            });
        }
    }
}
