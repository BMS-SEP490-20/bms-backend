using System.Security.Claims;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>UC71 read-only sample timeline; patient access is scoped by the authenticated account.</summary>
[ApiController]
[Route("api/sample-timelines")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Roles = "Admin,Administrator,Manager,Doctor,LabHead,LabTech,LabTechnician,Nurse,Phlebotomist,Receptionist,Patient")]
public sealed class SampleTimelineController(ISampleTimelineService service) : ControllerBase
{
    /// <summary>Lists accessible samples, without accepting an account identity from the client.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] SampleTimelineFilterDto filter, CancellationToken ct)
        => Ok(await service.ListAsync(filter, Viewer(), ct));

    /// <summary>Returns paginated recorded evidence and six progress stages; unknown times remain null.</summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, [FromQuery] SampleTimelinePageQuery page, CancellationToken ct)
    {
        var result = await service.GetAsync(code, page, Viewer(), ct);
        return result is null ? Missing() : Ok(result);
    }
    /// <summary>Returns staff-only scan evidence, explicitly distinguishing direct sample and shared-tag links.</summary>
    [HttpGet("{code}/rfid")]
    public async Task<IActionResult> Rfid(string code, [FromQuery] SampleTimelinePageQuery page, CancellationToken ct)
    {
        var result = await service.RfidAsync(code, page, Viewer(), ct);
        return result is null ? Missing() : Ok(result);
    }
    private NotFoundObjectResult Missing() => NotFound(new { message = "Không tìm thấy mẫu trong phạm vi được phép truy cập." });
    private SampleTimelineViewer Viewer() => new(User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
        User.FindFirstValue("role") ?? User.FindFirstValue(ClaimTypes.Role) ?? "");
}
