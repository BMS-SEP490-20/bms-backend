using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
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
// WHY: UC64: Admin only (C5). Audit rows are written only by the system through IAuditLogService.LogAsync;
// the public POST endpoint was removed (Quy trình ruling on DOT2 open question 1) so nobody can forge audit entries.
[Authorize(Roles = RoleNames.Admin)]
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
    }
}
