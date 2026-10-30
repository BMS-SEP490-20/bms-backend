using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using BMS.Application.DTOs.QC;
using BMS.Application.Interfaces;

namespace BMS.Api.Controllers.QCs
{
    /// <summary>
    /// Controller for configuring, inspecting, and resolving Quality Control (QC) flags.
    /// Triggered by laboratory analyzers or system out-of-bound threshold rules.
    /// Handled by Laboratory Users and Clinic Managers.
    /// </summary>
    [Route("TestOrder/[controller]")]
    [ApiController]
// WHY: UC74: Lab writes; Manager view. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
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
        [Authorize(Roles = RoleSets.LabOrManager)]
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? severity, [FromQuery] string? analyzerId, [FromQuery] string? parameter, CancellationToken ct)
        {
            var flags = await _service.GetAllAsync(status, severity, analyzerId, parameter, ct);
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
        [Authorize(Roles = RoleSets.LabOrManager)]
        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics(CancellationToken ct)
        {
            var stats = await _service.GetStatisticsAsync(ct);
            return Ok(new
            {
                message = "Retrieved QC flag statistics.",
                data = stats
            });
        }

        /// <summary>
        /// Retrieves a single QC flag by ID.
        /// </summary>
        [Authorize(Roles = RoleSets.LabOrManager)]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken ct)
        {
            var flag = await _service.GetByIdAsync(id, ct);
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
        [Authorize(Roles = RoleNames.Lab)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateQcFlagDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _service.CreateAsync(dto, ct);
            return StatusCode(201, new
            {
                message = "QC flag created successfully.",
                data = created
            });
        }

        /// <summary>
        /// Updates an existing QC flag, changes investigation status or records corrective actions.
        /// </summary>
        [Authorize(Roles = RoleNames.Lab)]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateQcFlagDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            var updated = await _service.UpdateAsync(id, dto, ct);
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
        [Authorize(Roles = RoleNames.Lab)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken ct)
        {
            var success = await _service.DeleteAsync(id, ct);
            if (!success)
                return NotFound(new { message = $"QC flag '{id}' not found." });

            return Ok(new { message = "QC flag deleted successfully.", success });
        }
    }
}
