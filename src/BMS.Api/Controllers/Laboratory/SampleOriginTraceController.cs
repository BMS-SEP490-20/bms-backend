using System.Threading.Tasks;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller for specimen origin traceability and chain of custody tracking (UC77 / Issue I3-088).
/// Provides complete lineage graph from patient appointment to analyzer result.
/// </summary>
[ApiController]
[Route("api/laboratory/trace")]
public class SampleOriginTraceController : ControllerBase
{
    private readonly ISampleOriginTraceService _traceService;

    public SampleOriginTraceController(ISampleOriginTraceService traceService)
    {
        _traceService = traceService;
    }

    /// <summary>
    /// Retrieves full traceability lineage nodes and chain of custody metrics for a specimen.
    /// </summary>
    [HttpGet("{specimenId}")]
    public async Task<IActionResult> GetOriginTrace(string specimenId)
    {
        var trace = await _traceService.GetOriginTraceBySpecimenIdAsync(specimenId);
        if (trace == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Trace record for specimen '{specimenId}' was not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = trace
        });
    }

    /// <summary>
    /// Retrieves a list of recent traceable specimens.
    /// </summary>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentTraceableSpecimens()
    {
        var list = await _traceService.GetRecentTraceableSpecimensAsync();
        return Ok(new
        {
            success = true,
            data = list
        });
    }

    /// <summary>
    /// Exports chain of custody audit certificate in PDF / printable format.
    /// </summary>
    [HttpGet("{specimenId}/export-pdf")]
    public async Task<IActionResult> ExportTracePdf(string specimenId)
    {
        var result = await _traceService.ExportTracePdfAsync(specimenId);
        if (result == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Unable to export trace PDF for specimen '{specimenId}'."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Trace PDF report generated successfully.",
            data = result
        });
    }
}
