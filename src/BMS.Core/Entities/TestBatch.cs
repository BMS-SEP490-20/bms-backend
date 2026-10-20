using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace BMS.Core.Entities;

public enum TestBatchStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3
}

public class WellPlatePosition
{
    [BsonElement("wellCoordinate")]
    public string WellCoordinate { get; set; } = string.Empty; // e.g. "A1", "A2", "B3"

    [BsonElement("isControlSample")]
    public bool IsControlSample { get; set; } = false;

    [BsonElement("controlLevel")]
    public string? ControlLevel { get; set; } // "IQC Level 1", "IQC Level 2"

    [BsonElement("sampleId")]
    public string? SampleId { get; set; }

    [BsonElement("barcode")]
    public string? Barcode { get; set; }

    [BsonElement("patientName")]
    public string? PatientName { get; set; }

    [BsonElement("priority")]
    public string? Priority { get; set; } // "Routine", "STAT"
}

/// <summary>
/// Laboratory analyzer testing batch conforming to ISO 15189 and IQC quality standards.
/// </summary>
[BsonIgnoreExtraElements]
public class TestBatch
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("batchCode")]
    public string BatchCode { get; set; } = string.Empty;

    [BsonElement("analyzerId")]
    public string AnalyzerId { get; set; } = string.Empty;

    [BsonElement("analyzerName")]
    public string AnalyzerName { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = "Biochemistry"; // Biochemistry, Hematology, Immunology

    [BsonElement("capacity")]
    public int Capacity { get; set; } = 48; // 24, 48, 96

    [BsonElement("status")]
    public TestBatchStatus Status { get; set; } = TestBatchStatus.Pending;

    [BsonElement("wells")]
    public List<WellPlatePosition> Wells { get; set; } = new();

    [BsonElement("technicianId")]
    public string TechnicianId { get; set; } = string.Empty;

    [BsonElement("technicianName")]
    public string? TechnicianName { get; set; }

    [BsonElement("startedAt")]
    public DateTime? StartedAt { get; set; }

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("cancelledAt")]
    public DateTime? CancelledAt { get; set; }

    [BsonElement("cancelReason")]
    public string? CancelReason { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
