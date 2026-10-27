using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TestOrder.Models.DTO;
using TestOrder.Services.Interface;
using System.Linq;

namespace TestOrder.Controllers
{
    [Route("TestOrder/[controller]")]
    [ApiController]

    public class BloodSampleController : ControllerBase
    {
        private readonly IBloodSampleService _service;

        public BloodSampleController(IBloodSampleService service)
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
                return NotFound(new { message = "Không có m?u máu nào du?c tìm th?y." });

            return Ok(new
            {
                message = "L?y danh sách m?u máu thành công.",
                page,
                pageSize,
                total,
                data = items
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var data = await _service.GetByIdAsync(id);
            if (data == null) return NotFound();
            return Ok(data);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] BloodSampleCreateDto dto)
        {
            if (dto == null)
                return BadRequest("Invalid blood sample data.");

            var created = await _service.CreateAsync(dto);

            if (created == null)
                return NotFound("Failed to create blood sample.");

            return CreatedAtAction(nameof(GetById), new { id = created.SampleId }, created);
        }


        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] BloodSampleUpdateDto dto)
        {
            var success = await _service.UpdateAsync(id, dto);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var success = await _service.DeleteAsync(id);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpGet("by-appointment/{appointmentId}")]
        public async Task<IActionResult> GetByAppointmentId(string appointmentId)
        {
            if (string.IsNullOrWhiteSpace(appointmentId))
                return BadRequest("Appointment ID is required.");

            var samples = await _service.GetBloodSampleByAppointmentIdAsync(appointmentId);

            if (samples == null || !samples.Any())
                return NotFound($"No blood samples found for appointment ID: {appointmentId}");

            return Ok(samples);
        } 
        /// <summary>
        /// Validates the quality and clinical integrity of a collected blood sample.
        /// Feature Task: Validate Sample (Backend).
        /// </summary>
        /// <param name="id">The unique identifier of the blood sample.</param>
        /// <param name="dto">The validation payload containing Status ('validated' or 'rejected'), verification notes, and LabUserId.</param>
        /// <returns>An <see cref="IActionResult"/> with a success message if validated.</returns>
        /// <response code="200">If the blood sample was successfully validated or rejected with recorded notes.</response>
        /// <response code="400">If the validation request payload is null or invalid.</response>
        /// <response code="404">If the specified blood sample cannot be found.</response>
        /// <example>
        /// <code>
        /// PUT /TestOrder/BloodSample/SA000123/validate
        /// Body: { "status": "validated", "notes": "Volume sufficient, no hemolysis", "labUserId": "tech_01" }
        /// </code>
        /// </example>
        [HttpPut("{id}/validate")]
        [Authorize(Roles = "lab_user,manager")]
        public async Task<IActionResult> ValidateSample(string id, [FromBody] BloodSampleValidateDto dto)
        {
            if (dto == null) return BadRequest("Invalid validation data.");
            var success = await _service.ValidateSampleAsync(id, dto.Status, dto.Notes, dto.LabUserId);
            if (!success) return NotFound($"Blood sample {id} not found.");
            return Ok(new { message = "Sample validated successfully." });
        }

        /// <summary>
        /// Retrieves standardized medical barcode label metadata for printing specimen labels.
        /// Feature Task: Print Barcodes (Backend).
        /// </summary>
        /// <param name="id">The unique identifier of the blood sample.</param>
        /// <returns>
        /// An <see cref="IActionResult"/> containing sampleId, barcode, sampleType, collectedAt, 
        /// and collectionTimestamp compliant with medical specimen labeling standards.
        /// </returns>
        /// <response code="200">Returns label metadata required for thermal printing and barcoding.</response>
        /// <response code="404">If the requested blood sample does not exist.</response>
        /// <example>
        /// <code>
        /// GET /TestOrder/BloodSample/SA000123/barcode-label
        /// </code>
        /// </example>
        [HttpGet("{id}/barcode-label")]
        [Authorize(Roles = "receptionist,lab_user,manager,admin")]
        public async Task<IActionResult> GetBarcodeLabel(string id)
        {
            var sample = await _service.GetByIdAsync(id);
            if (sample == null) return NotFound($"Blood sample {id} not found.");

            return Ok(new
            {
                sampleId = sample.SampleId,
                barcode = sample.Barcode,
                sampleType = sample.SampleType,
                collectedAt = sample.CollectedAt,
                collectionTimestamp = sample.CollectionTimestamp ?? (sample.CollectedAt?.ToString("yyyy-MM-dd HH:mm") ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")),
                status = sample.Status,
                volume = sample.Volume,
                appointmentId = sample.AppointmentId
            });
        }


    }
}

