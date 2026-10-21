using BMS.Application.DTOs.Inventory;

namespace BMS.Application.Interfaces;

/// <summary>UC56 inventory use cases.</summary>
public interface IInventoryService
{
    Task<InventoryItemDto> CreateAsync(CreateInventoryDto request, string actorId, string actorName, CancellationToken ct = default);
    Task<InventoryListDto> ListAsync(InventoryFilterDto filter, CancellationToken ct = default);
    Task<InventoryDetailDto?> GetAsync(string id, CancellationToken ct = default);
}
