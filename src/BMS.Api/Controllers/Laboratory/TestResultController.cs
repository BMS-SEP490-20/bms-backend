using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using BMS.Application.DTOs.TestResults;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory
{
    /// <summary>
    /// Legacy-route test result API (<c>TestOrder/TestResult</c>) used by the web lab screens,
    /// including Approve Test Results (I3-004). Errors are mapped by GlobalExceptionHandler;
    /// no exception details are returned to the client.
    /// </summary>
    [Route("TestOrder/[controller]")]
    [ApiController]
// WHY: UC46/UC49: enter Lab; view Lab, Doctor, Manager; approve Manager only (decision 1). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
    public class TestResultController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly ITestResultService _service;

        /// <summary>Creates the controller over the test result service.</summary>
        public TestResultController(ITestResultService service)
        {
            _service = service;
        }

        /// <summary>Lists test results with server-side paging.</summary>
        [Authorize(Roles = RoleSets.ClinicalStaff)]
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        {
            if (page <= 0 || pageSize <= 0)
                return BadRequest(new { message = "Giá trị phân trang không hợp lệ." });

            var (items, total) = await _service.GetPagedAsync(page, Math.Min(pageSize, MaxPageSize), ct);

            if (items.Count == 0)
                return NotFound(new { message = "Không có kết quả xét nghiệm nào được tìm thấy." });

            return Ok(new
            {
                message = "Lấy danh sách kết quả xét nghiệm thành công.",
                page,
                pageSize,
                total,
                data = items
            });
        }

        /// <summary>Gets a test result by id.</summary>
        [Authorize(Roles = RoleSets.ClinicalStaff)]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken ct)
        {
            var result = await _service.GetByIdAsync(id, ct);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy kết quả xét nghiệm." });

            return Ok(result);
        }

        /// <summary>Creates a test result with its parameters.</summary>
        [Authorize(Roles = RoleNames.Lab)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFullTestResultDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Dữ liệu gửi lên không hợp lệ (null)." });

            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    message = "Dữ liệu không hợp lệ.",
                    errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                });
            }

            var created = await _service.CreateAsync(dto, ct);
            return Ok(new { message = "Tạo kết quả xét nghiệm thành công.", data = created });
        }

        /// <summary>Updates a test result (partial).</summary>
        [Authorize(Roles = RoleNames.Lab)]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateTestResultDto dto, CancellationToken ct)
        {
            await _service.UpdateAsync(id, dto, ct);
            return Ok(new { message = "Cập nhật kết quả xét nghiệm thành công." });
        }

        /// <summary>Soft-deletes a test result.</summary>
        [Authorize(Roles = RoleNames.Lab)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken ct)
        {
            await _service.DeleteAsync(id, ct);
            return Ok(new { message = "Xóa kết quả xét nghiệm thành công." });
        }

        /// <summary>Lists the test results of one appointment.</summary>
        [Authorize(Roles = RoleSets.ClinicalStaff)]
        [HttpGet("by-appointment/{appointmentId}")]
        public async Task<IActionResult> GetByAppointmentId(string appointmentId, CancellationToken ct)
        {
            var results = await _service.GetByAppointmentIdAsync(appointmentId, ct);

            if (results.Count == 0)
                return NotFound(new { message = "Không tìm thấy kết quả xét nghiệm cho AppointmentId này." });

            return Ok(new
            {
                message = "Lấy kết quả xét nghiệm theo AppointmentId thành công.",
                appointmentId,
                count = results.Count,
                data = results
            });
        }

        /// <summary>
        /// Reviews, verifies, and digitally approves blood test results prior to publishing them to the patient.
        /// Manager only (decision 30/10 #1, matrix UC49); the approver identity comes from the token.
        /// </summary>
        /// <param name="id">Test result identifier.</param>
        /// <param name="dto">Approval payload containing approver info and digital stamp.</param>
        /// <param name="ct">Request cancellation token.</param>
        /// <returns>HTTP response containing approval status and updated result.</returns>
        // WHY: only the Manager signs results (decision 30/10 #1, UC49 "QL duy nhất").
        [Authorize(Roles = RoleNames.Manager)]
        [HttpPost("{id}/approve")]
        [HttpPut("{id}/approve")]
        public async Task<IActionResult> Approve(string id, [FromBody] ApproveTestResultDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Approval payload cannot be null." });

            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    message = "Invalid approval data.",
                    errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                });
            }

            // WHY (BR-APP-02): the signature must name the signed-in Manager, not whoever the body claims.
            dto.ApproverId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            dto.ApproverName = User.FindFirst("name")?.Value ?? dto.ApproverId;
            dto.ApproverRole = RoleNames.Manager;
            var result = await _service.ApproveAsync(id, dto, ct);
            return Ok(new
            {
                message = "Test result approved and published successfully.",
                data = result
            });
        }
    }
}
