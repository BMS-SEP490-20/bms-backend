using BMS.Application.DTOs.LabProfiles;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>UC61 Lab professional profiles for authorized administration and Lab management staff.</summary>
[ApiController]
[Route("api/lab-profiles")]
[Authorize(Roles = "Admin,Administrator,LabTechnician")]
public sealed class LabProfilesController(ILabProfileService service) : ControllerBase
{
    /// <summary>Returns the filtered profile directory with bounded pagination.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] LabProfileFilterDto filter, CancellationToken ct) => Ok(await service.ListAsync(filter, ct));

    /// <summary>Loads a professional profile by the linked account ID.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result is null ? NotFound(new { message = "Không tìm thấy hồ sơ Lab." }) : Ok(result);
    }

    /// <summary>Returns existing active Lab room references, without modifying assignments.</summary>
    [HttpGet("rooms")]
    public async Task<IActionResult> Rooms(CancellationToken ct) => Ok(await service.RoomsAsync(ct));

    /// <summary>Searches safe references to active Lab accounts; existing profiles remain reserved.</summary>
    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await service.AccountsAsync(search, page, pageSize, ct));

    /// <summary>Creates professional details for an existing account, without altering credentials.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateLabProfileDto input, CancellationToken ct)
    {
        var result = await service.CreateAsync(input, Actor(), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
    /// <summary>Updates professional details with optimistic concurrency; account identity cannot change.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateLabProfileDto input, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, input, Actor(), ct));

    /// <summary>Archives a profile with audit, preserving its account and historical assignments.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromBody] DeleteLabProfileDto input, CancellationToken ct)
    {
        await service.DeleteAsync(id, input, Actor(), ct);
        return NoContent();
    }

    private LabProfileActor Actor() => new(User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", User.Identity?.Name ?? "");
}
