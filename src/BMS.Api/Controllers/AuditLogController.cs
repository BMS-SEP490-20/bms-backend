using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using BMS.Application.DTOs.AuditLogs;
using BMS.Application.Interfaces;

namespace BMS.Api.Controllers
{
    /// <summary>
    /// Controller for viewing and querying security, access, and operational audit logs.
    /// Accessible by Admin and Officer (Manager) roles.
    /// </summary>
    [Route("TestOrder/[controller]")]
    [ApiController]
    public class AuditLogController : ControllerBase
    {
        private readonly IAuditLogService _service;

        /// <summary>Creates the controller over the audit log service.</summary>
        public AuditLogController(IAuditLogService service)
        {
            _service = service;
        }

        /// <summary>
        /// Retrieves paginated list of audit logs matching specified filters.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] AuditLogFilterDto filter, CancellationToken ct)
        {
            var (logs, totalCount) = await _service.GetAuditLogsAsync(filter, ct);
            return Ok(new
            {
                message = "Retrieved audit logs successfully.",
                totalCount,
                pageNumber = filter.PageNumber,
                pageSize = filter.PageSize,
                data = logs
            });
        }

        /// <summary>
        /// Retrieves statistical breakdown of audit logs.
        /// </summary>
        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics(CancellationToken ct)
        {
            var stats = await _service.GetStatisticsAsync(ct);
            return Ok(new
            {
                message = "Retrieved audit log statistics.",
                data = stats
            });
        }

        /// <summary>
        /// Retrieves an individual audit log by ID.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken ct)
        {
            var log = await _service.GetByIdAsync(id, ct);
            if (log == null)
                return NotFound(new { message = $"Audit log '{id}' not found." });

            return Ok(new
            {
                message = "Retrieved audit log.",
                data = log
            });
        }

        /// <summary>
        /// Dispatches a new audit log event.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAuditLogDto dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Payload cannot be null." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var created = await _service.LogAsync(dto, ct);
            return StatusCode(201, new
            {
                message = "Audit log recorded successfully.",
                data = created
            });
        }
    }
}
