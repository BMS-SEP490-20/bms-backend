using BMS.Application.DTOs.Inventory;

namespace BMS.Application.Interfaces;

/// <summary>UC56 inventory use cases.</summary>
public interface IInventoryService
{
    /// <summary>Creates a unique normalized code/lot identity with its opening balance and audit.</summary>
    Task<InventoryItemDto> CreateAsync(CreateInventoryDto request, string actorId, string actorName, CancellationToken ct = default);
    /// <summary>Updates the viewed revision; rejects stale requests without changing stock or audit.</summary>
    Task<InventoryItemDto> UpdateAsync(string id, UpdateInventoryDto request, string actorId, string actorName, CancellationToken ct = default);
    /// <summary>Archives an empty lot and preserves its complete management history.</summary>
    Task DeleteAsync(string id, DeleteInventoryDto request, string actorId, string actorName, CancellationToken ct = default);
    Task<InventoryListDto> ListAsync(InventoryFilterDto filter, CancellationToken ct = default);
    Task<InventoryDetailDto?> GetAsync(string id, CancellationToken ct = default);
}
