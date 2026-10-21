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
}

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
