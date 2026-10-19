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

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Sample ID is required.");

            var data = await _service.GetByIdAsync(id);

            if (data == null)
                return NotFound($"Blood sample with ID '{id}' not found.");

            return Ok(data);
        }


        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AppointmentCreateDto dto)
        {
            var result = await _service.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = result.AppointmentId }, result);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] AppointmentUpdateDto dto)
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
