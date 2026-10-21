using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>Read-only access to existing inventory collections.</summary>
public interface IInventoryRepository
{
    /// <summary>Filters and pages lots; counts refer to the complete filtered set, not the current page.</summary>
    Task<InventoryPage> ListAsync(InventorySearch search, DateTime today, CancellationToken ct = default);
    Task<Reagent?> GetAsync(string id, CancellationToken ct = default);
    Task<(List<InventoryTransaction> Items, long Total)> GetTransactionsAsync(string reagentCode, CancellationToken ct = default);
}

/// <summary>Validated search parameters. Dates are inclusive calendar dates in the expiry field.</summary>
public sealed record InventorySearch(string? Search, string? Status, DateTime? ExpiryFrom, DateTime? ExpiryTo, int Page, int PageSize);
public sealed record InventoryPage(List<Reagent> Items, long Total, long LowStock, long ExpiringSoon, long Expired);
