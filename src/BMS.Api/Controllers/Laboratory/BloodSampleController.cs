using BMS.Application.DTOs.BloodSamples;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory
{
    /// <summary>
    /// Blood sample API used by the web sample screens.
    /// Single controller merging the two identical copies that came from Print Barcodes (#58,
    /// Controllers/Print_Barcode) and Validate Sample (#73, Controllers/validate_sample); all endpoints of both are kept.
    /// </summary>
    [Route("TestOrder/[controller]")]
    [ApiController]
    public class BloodSampleController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly IBloodSampleService _service;

        /// <summary>Creates the controller over the blood sample service.</summary>
        public BloodSampleController(IBloodSampleService service)
        {
            _service = service;
        }

        /// <summary>Lists blood samples with server-side paging (newest first).</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            if (page <= 0 || pageSize <= 0)
                return BadRequest(new { message = "Giá trị phân trang không hợp lệ." });

            var (items, total) = await _service.GetPagedAsync(page, Math.Min(pageSize, MaxPageSize), ct);

            if (items.Count == 0)
                return NotFound(new { message = "Không có mẫu máu nào được tìm thấy." });

            return Ok(new
            {
                message = "Lấy danh sách mẫu máu thành công.",
                page,
                pageSize,
                total,
                data = items
            });
        }

        /// <summary>Gets a blood sample by id or barcode.</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken ct)
        {
            var data = await _service.GetByIdAsync(id, ct);
            if (data == null) return NotFound();
            return Ok(data);
        }

        /// <summary>Registers a blood sample; the server generates a barcode when none is sent.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] BloodSampleCreateDto dto, CancellationToken ct)
        {
            if (dto == null || !ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.SampleId }, created);
        }

        /// <summary>Partially updates a blood sample (null fields unchanged).</summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] BloodSampleUpdateDto dto, CancellationToken ct)
        {
            var success = await _service.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        /// <summary>Soft-deletes a blood sample (kept for audit / timeline).</summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken ct)
        {
            var success = await _service.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        /// <summary>Lists the blood samples of one appointment / test order.</summary>
        [HttpGet("by-appointment/{appointmentId}")]
        public async Task<IActionResult> GetByAppointmentId(string appointmentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(appointmentId))
                return BadRequest("Appointment ID is required.");

            var samples = await _service.GetBloodSampleByAppointmentIdAsync(appointmentId, ct);

            if (samples.Count == 0)
                return NotFound("No blood samples found for this appointment.");

            return Ok(samples);
        }

        /// <summary>
        /// Validates the quality and clinical integrity of a collected blood sample.
        /// Feature Task: Validate Sample (Backend).
        /// </summary>
        /// <param name="id">The unique identifier of the blood sample.</param>
        /// <param name="dto">The validation payload containing Status ('validated' or 'rejected'), verification notes, and LabUserId.</param>
        /// <param name="ct">Request cancellation token.</param>
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
        // TODO(senior-dev, Đợt 2): role names "lab_user,manager" do not match the agreed 6-role constants.
        [Authorize(Roles = "lab_user,manager")]
        public async Task<IActionResult> ValidateSample(string id, [FromBody] BloodSampleValidateDto dto, CancellationToken ct)
        {
            if (dto == null) return BadRequest("Invalid validation data.");
            var success = await _service.ValidateSampleAsync(id, dto.Status, dto.Notes, dto.LabUserId, ct);
            if (!success) return NotFound($"Blood sample {id} not found.");
            return Ok(new { message = "Sample validated successfully." });
        }

        /// <summary>
        /// Retrieves standardized medical barcode label metadata for printing specimen labels.
        /// Feature Task: Print Barcodes (Backend).
        /// </summary>
        /// <param name="id">The unique identifier of the blood sample.</param>
        /// <param name="ct">Request cancellation token.</param>
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
        // TODO(senior-dev, Đợt 2): role names do not match the agreed 6-role constants.
        [Authorize(Roles = "receptionist,lab_user,manager,admin")]
        public async Task<IActionResult> GetBarcodeLabel(string id, CancellationToken ct)
        {
            var sample = await _service.GetByIdAsync(id, ct);
            if (sample == null) return NotFound($"Blood sample {id} not found.");

            return Ok(new
            {
                sampleId = sample.SampleId,
                barcode = sample.Barcode,
                sampleType = sample.SampleType,
                collectedAt = sample.CollectedAt,
                collectionTimestamp = sample.CollectionTimestamp,
                status = sample.Status,
                volume = sample.Volume,
                appointmentId = sample.AppointmentId
            });
        }
    }
}
