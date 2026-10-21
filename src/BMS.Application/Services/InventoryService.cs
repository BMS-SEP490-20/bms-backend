using BMS.Application.DTOs.Inventory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>Derives UC56 warnings from stock and expiry values instead of the potentially stale stored status.</summary>
public sealed class InventoryService(IInventoryRepository repository, TimeProvider clock) : IInventoryService
{
    private DateTime Today => clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).Date;

    public async Task<InventoryListDto> ListAsync(InventoryFilterDto filter, CancellationToken ct = default)
    {
        if (filter.Page < 1 || filter.Page > 10000 || filter.PageSize < 1 || filter.PageSize > 100 ||
            filter.Search?.Length > 100 ||
            (!string.IsNullOrEmpty(filter.Status) && filter.Status is not ("LOW_STOCK" or "EXPIRING_SOON" or "EXPIRED" or "AVAILABLE" or "UNKNOWN_EXPIRY")) ||
            (filter.ExpiryFrom.HasValue && filter.ExpiryTo.HasValue && filter.ExpiryFrom.Value.Date > filter.ExpiryTo.Value.Date) ||
            filter.ExpiryTo?.Date == DateTime.MaxValue.Date)
            throw new DomainValidationException("INVALID_INVENTORY_FILTER", "Bộ lọc không hợp lệ. Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
        var today = Today;
        var result = await repository.ListAsync(new(filter.Search?.Trim(), filter.Status,
            filter.ExpiryFrom?.Date, filter.ExpiryTo?.Date, filter.Page, filter.PageSize), today, ct);
        return new(result.Items.Select(x => Map(x, today)).ToList(),
            new(result.Total, result.LowStock, result.ExpiringSoon, result.Expired),
            filter.Page, filter.PageSize, result.Total, (int)Math.Ceiling((double)result.Total / filter.PageSize), today);
    }

    public async Task<InventoryDetailDto?> GetAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
            throw new DomainValidationException("INVALID_INVENTORY_ID", "Mã vật tư không hợp lệ.");
        var item = await repository.GetAsync(id.Trim(), ct);
        if (item is null) return null;
        var (transactions, total) = await repository.GetTransactionsAsync(item.Code, ct);
        var today = Today;
        return new(Map(item, today), transactions.Select(x => new InventoryTransactionDto(x.Id,
            x.Type, x.Quantity, x.UnitPrice, x.Department, x.Timestamp)).ToList(), total, today);
    }

    private static InventoryItemDto Map(Reagent item, DateTime today)
    {
        // Printed expiry dates remain valid through that calendar day. BR-WAR-01 warns when fewer than 30 days remain.
        var expiry = item.ExpiryDate?.Date;
        var status = expiry is null ? "UNKNOWN_EXPIRY" : expiry < today ? "EXPIRED"
            : expiry < today.AddDays(30) ? "EXPIRING_SOON" : "AVAILABLE";
        return new(item.Id, item.Code, item.Name, item.Manufacturer, item.LotNumber,
            item.Quantity, item.Unit, item.MinimumStock, expiry, status, item.Quantity <= item.MinimumStock);
    }
}
