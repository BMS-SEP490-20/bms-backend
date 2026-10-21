using BMS.Application.DTOs.Inventory;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Inventory;

/// <summary>
/// UC56 read-only inventory API for warehouse, laboratory and management staff.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/inventory")]
[Authorize(Roles = "Admin,Administrator,Manager,WarehouseStaff,LabHead,LabTech,LabTechnician,LaboratoryUser,LabUser")]
public sealed class ReagentsController(IInventoryService service) : ControllerBase
{
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
