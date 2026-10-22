using System.Security.Claims;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace BMS.Api.Controllers.Laboratory;

/// <summary>UC76 signed test history; ownership and disclosure rules are enforced by the service/repository.</summary>
[ApiController, Route("api/test-history")]
[Authorize(Roles = "Admin,Administrator,Manager,Doctor,LabHead,LabTech,LabTechnician,Patient")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TestHistoryController(ITestHistoryService service) : ControllerBase
{
    /// <summary>Returns a bounded history page in the authenticated viewer's permitted scope.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] TestHistoryFilterDto filter, CancellationToken ct) => Ok(await service.ListAsync(filter, Viewer(), ct));
    /// <summary>Returns signed published values or metadata-only processing state; inaccessible IDs share 404.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var result = await service.GetAsync(id, Viewer(), ct);
        return result is null ? NotFound(new { message = "Không tìm thấy kết quả trong phạm vi được phép truy cập." }) : Ok(result);
    }
    private TestHistoryViewer Viewer() => new(User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
        User.FindFirstValue("role") ?? User.FindFirstValue(ClaimTypes.Role) ?? "", HttpContext.Connection.RemoteIpAddress?.ToString());
}
