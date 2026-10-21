using BMS.Application.DTOs.Inventory;

namespace BMS.Application.Interfaces;

/// <summary>UC56 read-only inventory use cases.</summary>
public interface IInventoryService
{
    Task<InventoryListDto> ListAsync(InventoryFilterDto filter, CancellationToken ct = default);
    Task<InventoryDetailDto?> GetAsync(string id, CancellationToken ct = default);
}
