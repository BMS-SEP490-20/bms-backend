using Microsoft.AspNetCore.Mvc;
using TestOrder.Models.DTO;
using TestOrder.Services.Interface;
using System.Linq;
using Testorder_service.Models.DTO;

namespace TestOrder.Controllers
{
    [Route("TestOrder/[controller]")]
    [ApiController]
    public class TestResultController : ControllerBase
    {
        private readonly ITestResultService _service;

        public TestResultController(ITestResultService service)
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

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var result = await _service.GetByIdAsync(id);
            if (result == null)
                return NotFound(new { message = "Không tìm thấy kết quả xét nghiệm." });

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFullTestResultDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { message = "Dữ liệu gửi lên không hợp lệ (null)." });
                }               
                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        message = "Dữ liệu không hợp lệ.",
                        errors = ModelState.Values.SelectMany(v => v.Errors)
                                                  .Select(e => e.ErrorMessage)
                    });
                }
                await _service.CreateAsync(dto);

                return Ok(new { message = "Tạo kết quả xét nghiệm thành công." });
            }
            catch (KeyNotFoundException ex)
            {  
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Có lỗi xảy ra khi tạo kết quả xét nghiệm.",
                    detail = ex.Message 
                });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateTestResultDto dto)
        {
            try
            {
                await _service.UpdateAsync(id, dto);
                return Ok(new { message = "Cập nhật kết quả xét nghiệm thành công." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Cập nhật kết quả xét nghiệm thất bại.", error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                await _service.DeleteAsync(id);
                return Ok(new { message = "Xóa kết quả xét nghiệm thành công." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Xóa kết quả xét nghiệm thất bại.", error = ex.Message });
            }
        }

        [HttpGet("by-appointment/{appointmentId}")]
        public async Task<IActionResult> GetByAppointmentId(string appointmentId)
        {
            var results = await _service.GetByAppointmentIdAsync(appointmentId);

            if (results == null || !results.Any())
                return NotFound(new { message = "Không tìm thấy kết quả xét nghiệm cho AppointmentId này." });

            return Ok(new
            {
                message = "Lấy kết quả xét nghiệm theo AppointmentId thành công.",
                appointmentId,
                count = results.Count(),
                data = results
            });
        }

        /// <summary>
        /// Reviews, verifies, and digitally approves blood test results prior to publishing them to the patient.
        /// Intended for Doctor and Lab Manager roles.
        /// </summary>
        /// <param name="id">Test result identifier.</param>
        /// <param name="dto">Approval payload containing approver info and digital stamp.</param>
        /// <returns>HTTP response containing approval status and updated result.</returns>
        [HttpPost("{id}/approve")]
        [HttpPut("{id}/approve")]
        public async Task<IActionResult> Approve(string id, [FromBody] ApproveTestResultDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { message = "Approval payload cannot be null." });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(new
                    {
                        message = "Invalid approval data.",
                        errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                    });
                }

                var result = await _service.ApproveAsync(id, dto);
                return Ok(new
                {
                    message = "Test result approved and published successfully.",
                    data = result
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Failed to approve test result.", error = ex.Message });
            }
        }
    }
}
