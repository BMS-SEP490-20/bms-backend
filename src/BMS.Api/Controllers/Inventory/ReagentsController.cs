using BMS.Application.DTOs.Inventory;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BMS.Api.Controllers.Inventory;

/// <summary>
/// UC56 inventory API; stock mutations are restricted to warehouse and management staff.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/inventory")]
[Authorize(Roles = "Admin,Administrator,Manager,WarehouseStaff,LabHead,LabTech,LabTechnician,LaboratoryUser,LabUser")]
public sealed class ReagentsController(IInventoryService service) : ControllerBase
{
    private string? ActorId => User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
    private string ActorName => User.FindFirstValue("name") ?? User.FindFirstValue(ClaimTypes.Name) ?? "Nhân viên kho";

    /// <summary>Creates a lot with its opening stock and an immutable audit entry.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Administrator,Manager,WarehouseStaff")]
    [ProducesResponseType(typeof(InventoryItemDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateInventoryDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ActorId)) return Unauthorized();
        var item = await service.CreateAsync(request, ActorId, ActorName, ct);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }
    /// <summary>
    /// Retrieves reagent stock levels and lot expiration tracking.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(InventoryListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReagents([FromQuery] InventoryFilterDto filter, CancellationToken ct)
    {
        return Ok(await service.ListAsync(filter, ct));
    }

    /// <summary>Returns a lot and the latest 20 movements for its reagent code.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(InventoryDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var item = await service.GetAsync(id, ct);
        return item is null ? NotFound(new { message = "Không tìm thấy vật tư hoặc lô hóa chất." }) : Ok(item);
    }
}
