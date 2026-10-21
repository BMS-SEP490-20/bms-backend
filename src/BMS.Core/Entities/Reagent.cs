using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>Existing reagent lot stored in the <c>reagents</c> collection.</summary>
[BsonIgnoreExtraElements]
public sealed class Reagent
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    [BsonElement("reagent_code")]
    public string Code { get; set; } = string.Empty;
    [BsonElement("reagent_name")]
    public string Name { get; set; } = string.Empty;
    [BsonElement("manufacturer")]
    public string Manufacturer { get; set; } = string.Empty;
    [BsonElement("lot_number")]
    public string LotNumber { get; set; } = string.Empty;
    [BsonElement("quantity_in_stock")]
    public decimal Quantity { get; set; }
    [BsonElement("unit")]
    public string Unit { get; set; } = string.Empty;
    [BsonElement("min_threshold")]
    public decimal MinimumStock { get; set; }
    [BsonElement("expiry_date")]
    public DateTime? ExpiryDate { get; set; }
    [BsonElement("revision")]
    public long Revision { get; set; }
    [BsonElement("changes")]
    public List<InventoryChange> Changes { get; set; } = new();
    [BsonElement("is_deleted")]
    public bool IsDeleted { get; set; }
    [BsonElement("deleted_at")]
    public DateTime? DeletedAt { get; set; }
}

/// <summary>Audit stored with the lot so a stock change and its evidence commit atomically.</summary>
public sealed class InventoryChange
{
    public string Action { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime At { get; set; }
    public InventorySnapshot? Before { get; set; }
    public InventorySnapshot? After { get; set; }
}

/// <summary>Stock and metadata captured before/after an inventory operation.</summary>
public sealed record InventorySnapshot(string Name, string Manufacturer, decimal Quantity,
    decimal MinimumStock, DateTime? ExpiryDate);

/// <summary>Existing reagent-level stock movement, not a per-lot receipt.</summary>
[BsonIgnoreExtraElements]
public sealed class InventoryTransaction
{
    [BsonId]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string? Id { get; set; }
    [BsonElement("reagent_code")]
    public string ReagentCode { get; set; } = string.Empty;
    [BsonElement("transaction_type")]
    public string Type { get; set; } = string.Empty;
    [BsonElement("quantity")]
    public decimal Quantity { get; set; }
    [BsonElement("unit_price")]
    public decimal? UnitPrice { get; set; }
    [BsonElement("department")]
    public string Department { get; set; } = string.Empty;
    [BsonElement("timestamp")]
    public DateTime? Timestamp { get; set; }
}
