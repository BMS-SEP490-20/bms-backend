using BMS.Core.Constants;
using BMS.Core.Exceptions;
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
// WHY: UC39/UC44: CRUD Lab; validate Lab, Manager; barcode label Lab, Receptionist. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
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
        [Authorize(Roles = RoleSets.LabOrManager)]
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            try
            {
                if (page <= 0 || pageSize <= 0)
                    return BadRequest(new { success = false, message = "Giá trị phân trang không hợp lệ." });

                var (items, total) = await _service.GetPagedAsync(page, Math.Min(pageSize, MaxPageSize), ct);

                return Ok(new
                {
                    success = true,
                    message = "Lấy danh sách mẫu máu thành công.",
                    page,
                    pageSize,
                    total,
                    data = items
                });
            }
            catch (AppException ex)
            {
                return StatusCode((int)ex.StatusCode, new { success = false, message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể tải danh sách mẫu máu." });
            }
        }

        /// <summary>Gets a blood sample by id or barcode.</summary>
        [Authorize(Roles = RoleSets.LabOrManager)]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken ct)
        {
            try
            {
                var data = await _service.GetByIdAsync(id, ct);
                if (data == null) return NotFound(new { success = false, message = $"Không tìm thấy mẫu máu '{id}'." });
                return Ok(data);
            }
            catch (AppException ex)
            {
                return StatusCode((int)ex.StatusCode, new { success = false, message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể lấy thông tin mẫu máu." });
            }
        }

        /// <summary>Registers a blood sample; the server generates a barcode when none is sent.</summary>
        [Authorize(Roles = RoleNames.Lab)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] BloodSampleCreateDto dto, CancellationToken ct)
        {
            try
            {
                if (dto == null || !ModelState.IsValid)
                    return BadRequest(new { success = false, message = "Dữ liệu mẫu máu không hợp lệ." });

                var created = await _service.CreateAsync(dto, ct);
                return CreatedAtAction(nameof(GetById), new { id = created.SampleId }, created);
            }
            catch (AppException ex)
            {
                return StatusCode((int)ex.StatusCode, new { success = false, message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể tạo mẫu máu mới." });
            }
        }

        /// <summary>Partially updates a blood sample (null fields unchanged).</summary>
        [Authorize(Roles = RoleSets.LabOrManager)]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] BloodSampleUpdateDto dto, CancellationToken ct)
        {
            try
            {
                var success = await _service.UpdateAsync(id, dto, ct);
                if (!success) return NotFound(new { success = false, message = $"Không tìm thấy mẫu máu '{id}'." });
                return Ok(new { success = true, message = "Cập nhật mẫu máu thành công." });
            }
            catch (AppException ex)
            {
                return StatusCode((int)ex.StatusCode, new { success = false, message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể cập nhật mẫu máu." });
            }
        }

        /// <summary>Soft-deletes a blood sample (kept for audit / timeline).</summary>
        [Authorize(Roles = RoleNames.Lab)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken ct)
        {
            try
            {
                var success = await _service.DeleteAsync(id, ct);
                if (!success) return NotFound(new { success = false, message = $"Không tìm thấy mẫu máu '{id}'." });
                return NoContent();
            }
            catch (AppException ex)
            {
                return StatusCode((int)ex.StatusCode, new { success = false, message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể xóa mẫu máu." });
            }
        }

        /// <summary>Lists the blood samples of one appointment / test order.</summary>
        [Authorize(Roles = RoleSets.LabOrManager)]
        [HttpGet("by-appointment/{appointmentId}")]
        public async Task<IActionResult> GetByAppointmentId(string appointmentId, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(appointmentId))
                    return BadRequest(new { success = false, message = "Appointment ID is required." });

                var samples = await _service.GetBloodSampleByAppointmentIdAsync(appointmentId, ct);

                if (samples.Count == 0)
                    return NotFound(new { success = false, message = "No blood samples found for this appointment." });

                return Ok(samples);
            }
            catch (AppException ex)
            {
                return StatusCode((int)ex.StatusCode, new { success = false, message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể lấy danh sách mẫu máu theo lịch hẹn." });
            }
        }

        /// <summary>
        /// Validates the quality and clinical integrity of a collected blood sample.
        /// Feature Task: Validate Sample (Backend).
        /// </summary>
        [Authorize(Roles = RoleSets.LabOrManager)]
        [HttpPut("{id}/validate")]
        public async Task<IActionResult> ValidateSample(string id, [FromBody] BloodSampleValidateDto dto, CancellationToken ct)
        {
            try
            {
                if (dto == null) return BadRequest(new { success = false, message = "Dữ liệu xác nhận mẫu không hợp lệ." });
                var statusToApply = string.IsNullOrWhiteSpace(dto.Status) ? "validated" : dto.Status;
                var success = await _service.ValidateSampleAsync(id, statusToApply, dto.Notes, dto.LabUserId, ct);
                if (!success) return NotFound(new { success = false, message = $"Không tìm thấy mẫu máu '{id}'." });
                return Ok(new { success = true, message = "Xác nhận chất lượng mẫu xét nghiệm thành công." });
            }
            catch (AppException ex)
            {
                return StatusCode((int)ex.StatusCode, new { success = false, message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể xác nhận mẫu xét nghiệm." });
            }
        }

        /// <summary>
        /// Retrieves standardized medical barcode label metadata for printing specimen labels.
        /// Feature Task: Print Barcodes (Backend).
        /// </summary>
        [Authorize(Roles = RoleSets.LabOrReception)]
        [HttpGet("{id}/barcode-label")]
        public async Task<IActionResult> GetBarcodeLabel(string id, CancellationToken ct)
        {
            try
            {
                var sample = await _service.GetByIdAsync(id, ct);
                if (sample == null) return NotFound(new { success = false, message = $"Blood sample {id} not found." });

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
            catch (AppException ex)
            {
                return StatusCode((int)ex.StatusCode, new { success = false, message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Không thể tải nhãn mã vạch mẫu máu." });
            }
        }
    }
}
