using Microsoft.AspNetCore.Mvc;
using TestOrder.Models.DTO;
using TestOrder.Services.Interface;
using System.Linq;

namespace TestOrder.Controllers
{

    [Route("TestOrder/[controller]")]
    [ApiController]
    public class AppointmentController : ControllerBase
    {
        private readonly IAppointmentService _service;

        public AppointmentController(IAppointmentService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var data = await _service.GetAllAsync();
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
        public async Task<IActionResult> GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Appointment ID is required.");

            var data = await _service.GetByIdAsync(id);

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
        public async Task<IActionResult> Create([FromBody] AppointmentCreateDto dto)
        {
            var result = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = result.AppointmentId }, result);
        }

        /// <summary>
        /// Updates existing appointment details or sets its status to 'checked_in' during Scan QR Check-in.
        /// </summary>
        /// <param name="id">The unique identifier of the appointment to update.</param>
        /// <param name="dto">The update payload containing updated time, status, or reschedule notes.</param>
        /// <returns>204 NoContent if successful; 404 NotFound if the appointment does not exist.</returns>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] AppointmentUpdateDto dto)
        {
            var success = await _service.UpdateAsync(id, dto);
            if (!success) return NotFound();
            return NoContent();
        }

        /// <summary>
        /// Deletes an appointment from the system.
        /// </summary>
        /// <param name="id">The unique identifier of the appointment to delete.</param>
        /// <returns>204 NoContent on success; 404 NotFound if the record is missing.</returns>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var success = await _service.DeleteAsync(id);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpGet("patient/{patientId}")]
        public async Task<IActionResult> GetByPatientId(
            string patientId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var data = await _service.GetByPatientIdAsync(patientId);
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
