using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Inventory;

/// <summary>UC56 list filters. ExpiryFrom/To are inclusive expiry dates, not movement dates.</summary>
public sealed class InventoryFilterDto
{
    [StringLength(100)] public string? Search { get; set; }
    [RegularExpression("^(LOW_STOCK|EXPIRING_SOON|EXPIRED|AVAILABLE|UNKNOWN_EXPIRY)$")]
    public string? Status { get; set; }
    public DateTime? ExpiryFrom { get; set; }
    public DateTime? ExpiryTo { get; set; }
    [Range(1, 10000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

/// <summary>A reagent lot with expiry warnings calculated by the server on the current clinic day.</summary>
public sealed record InventoryItemDto(string Id, string Code, string Name, string Manufacturer,
    string LotNumber, decimal Quantity, string Unit, decimal MinimumStock, DateTime? ExpiryDate,
    string ExpiryStatus, bool IsLowStock, long Revision);
public sealed record InventorySummaryDto(long Total, long LowStock, long ExpiringSoon, long Expired);
public sealed record InventoryListDto(List<InventoryItemDto> Items, InventorySummaryDto Summary,
    int Page, int PageSize, long Total, int TotalPages, DateTime AsOfDate);
public sealed record InventoryTransactionDto(string? Id, string Type, decimal Quantity,
    decimal? UnitPrice, string Department, DateTime? Timestamp);
/// <summary>Movement history is shared by reagent code because existing transactions do not store a lot number.</summary>
public sealed record InventoryDetailDto(InventoryItemDto Item, List<InventoryTransactionDto> Transactions,
    long TransactionCount, DateTime AsOfDate, List<InventoryChangeDto> Changes);

/// <summary>Values editable by inventory managers. Identity fields are provided separately on create.</summary>
public class InventoryValuesDto
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(100)] public string Manufacturer { get; set; } = string.Empty;
    [Range(typeof(decimal), "0", "1000000000")] public decimal Quantity { get; set; }
    [Range(typeof(decimal), "0", "1000000000")] public decimal MinimumStock { get; set; }
    [Required] public DateTime? ExpiryDate { get; set; }
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = string.Empty;
}

/// <summary>Creates a lot with an opening balance and server-derived audit identity.</summary>
public sealed class CreateInventoryDto : InventoryValuesDto
{
    [Required, RegularExpression("^[A-Za-z0-9][A-Za-z0-9._/-]{0,49}$")]
    public string Code { get; set; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z0-9][A-Za-z0-9._/-]{0,79}$")]
    public string LotNumber { get; set; } = string.Empty;
    [Required, StringLength(30)] public string Unit { get; set; } = string.Empty;
}

/// <summary>Updates values on the exact revision viewed by the client; lot identity and unit remain immutable.</summary>
public sealed class UpdateInventoryDto : InventoryValuesDto
{
    [Range(0, long.MaxValue)] public long ExpectedRevision { get; set; }
}

public sealed record InventoryChangeDto(string Action, string ActorName, string Reason, DateTime At,
    decimal? BeforeQuantity, decimal? AfterQuantity);
