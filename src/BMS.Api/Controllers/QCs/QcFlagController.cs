using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Testorder_service.Models.DTO;
using Testorder_service.Services.Interface;

namespace Testorder_service.Controllers
{
    /// <summary>
    /// Controller for configuring, inspecting, and resolving Quality Control (QC) flags.
    /// Triggered by laboratory analyzers or system out-of-bound threshold rules.
    /// Handled by Laboratory Users and Clinic Managers.
    /// </summary>
    [Route("TestOrder/[controller]")]
    [ApiController]
    public class QcFlagController : ControllerBase
    {
        private readonly IQcFlagService _service;

        public QcFlagController(IQcFlagService service)
        {
            _service = service;
        }

        /// <summary>
        /// Retrieves list of QC flags filtered by status, severity, analyzer, or parameter.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? severity, [FromQuery] string? analyzerId, [FromQuery] string? parameter)
        {
            var flags = await _service.GetAllAsync(status, severity, analyzerId, parameter);
            return Ok(new
            {
                message = "Retrieved QC flags successfully.",
                count = flags.Count(),
                data = flags
            });
        }

        /// <summary>
        /// Retrieves statistical counts for QC flags.
        /// </summary>
        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics()
        {
            var stats = await _service.GetStatisticsAsync();
            return Ok(new
            {
                message = "Retrieved QC flag statistics.",
                data = stats
            });
        }

        /// <summary>
        /// Retrieves a single QC flag by ID.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var flag = await _service.GetByIdAsync(id);
            if (flag == null)
                return NotFound(new { message = $"QC flag '{id}' not found." });

            return Ok(new
            {
                message = "Retrieved QC flag.",
                data = flag
            });
        }

        /// <summary>
        /// Creates/triggers a new QC flag manually or from analyzer rules.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateQcFlagDto dto)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _service.CreateAsync(dto);
            return StatusCode(201, new
            {
                message = "QC flag created successfully.",
                data = created
            });
        }

        /// <summary>
        /// Updates an existing QC flag, changes investigation status or records corrective actions.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateQcFlagDto dto)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            var updated = await _service.UpdateAsync(id, dto);
            if (updated == null)
                return NotFound(new { message = $"QC flag '{id}' not found." });

            return Ok(new
            {
                message = "QC flag updated successfully.",
                data = updated
            });
        }

        /// <summary>
        /// Deletes a QC flag record.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var success = await _service.DeleteAsync(id);
            if (!success)
                return NotFound(new { message = $"QC flag '{id}' not found." });

            return Ok(new { message = "QC flag deleted successfully.", success });
        }
    }
}
