using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Individual milestone node in the full specimen origin and processing lineage graph (UC77).
/// </summary>
[BsonIgnoreExtraElements]
public class TraceNode
{
    [BsonElement("node_type")]
    public string NodeType { get; set; } = string.Empty; // "Patient", "Doctor", "Order", "Collection", "Specimen", "Analyzer", "Result", "Status"

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("badge")]
    public string? Badge { get; set; }

    [BsonElement("badge_type")]
    public string BadgeType { get; set; } = "default"; // "patient", "doctor", "order", "warn", "err", "ok"

    [BsonElement("timestamp")]
    public DateTime? Timestamp { get; set; }

    [BsonElement("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Chain of custody audit record verifying end-to-end specimen security and integrity.
/// </summary>
[BsonIgnoreExtraElements]
public class ChainOfCustody
{
    [BsonElement("integrity")]
    public string Integrity { get; set; } = "Unbroken";

    [BsonElement("barcode_scans_count")]
    public int BarcodeScansCount { get; set; } = 7;

    [BsonElement("transport_duration")]
    public string TransportDuration { get; set; } = "12 min (Room 02 → Chem bench)";

    [BsonElement("analyzer_model")]
    public string AnalyzerModel { get; set; } = "BC-01 · Calibrated 07:30";

    [BsonElement("reagent_lot")]
    public string ReagentLot { get; set; } = "LOT-7721 · Exp 31 Dec 2026";

    [BsonElement("qc_status")]
    public string QcStatus { get; set; } = "QCF-1142";

    [BsonElement("rerun_count")]
    public int RerunCount { get; set; } = 1;

    [BsonElement("notes_count")]
    public int NotesCount { get; set; } = 3;
}

/// <summary>
/// Full traceability domain entity from patient order to final laboratory test result (UC77 / Trace Sample Origin).
/// Maps to MongoDB collection "sample_origin_traces".
/// </summary>
[BsonIgnoreExtraElements]
public class SampleOriginTrace
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("specimen_id")]
    public string SpecimenId { get; set; } = string.Empty;

    [BsonElement("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("patient_id")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patient_name")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("nodes")]
    public List<TraceNode> Nodes { get; set; } = new();

    [BsonElement("custody")]
    public ChainOfCustody Custody { get; set; } = new();

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
