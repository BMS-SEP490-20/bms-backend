using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Predefined benchmark targets for turnaround time by clinical urgency (ISO 15189 compliance).
/// </summary>
public static class TatBenchmarks
{
    public const int DefaultStatTargetMinutes = 60;     // Emergency / STAT target: <= 60 minutes
    public const int DefaultUrgentTargetMinutes = 120;  // Urgent priority target: <= 120 minutes
    public const int DefaultNormalTargetMinutes = 240;  // Routine / Normal target: <= 240 minutes (4 hours)
}

/// <summary>
/// Laboratory department categories for TAT aggregation.
/// </summary>
public enum LaboratoryDepartment
{
    All = 0,
    Biochemistry = 1,
    Hematology = 2,
    Immunology = 3,
    Microbiology = 4,
    Urinalysis = 5,
    Molecular = 6
}

/// <summary>
/// Entity representing an audited TAT benchmark snapshot or recorded calculation record.
/// Maps to MongoDB collection "tat_statistics_records".
/// </summary>
[BsonIgnoreExtraElements]
public class TatBenchmarkRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("sample_id")]
    public string SampleId { get; set; } = string.Empty;

    [BsonElement("barcode")]
    public string Barcode { get; set; } = string.Empty;

    [BsonElement("patient_name")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("test_package")]
    public string TestPackage { get; set; } = string.Empty;

    [BsonElement("department")]
    public string Department { get; set; } = "Biochemistry";

    [BsonElement("priority")]
    public string Priority { get; set; } = "Normal";

    [BsonElement("lab_room")]
    public string? LabRoom { get; set; }

    [BsonElement("collected_at")]
    public DateTime CollectedAt { get; set; }

    [BsonElement("received_at")]
    public DateTime? ReceivedAt { get; set; }

    [BsonElement("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    [BsonElement("completed_at")]
    public DateTime CompletedAt { get; set; }

    [BsonElement("pre_analytical_minutes")]
    public double PreAnalyticalMinutes { get; set; }

    [BsonElement("analytical_minutes")]
    public double AnalyticalMinutes { get; set; }

    [BsonElement("post_analytical_minutes")]
    public double PostAnalyticalMinutes { get; set; }

    [BsonElement("total_tat_minutes")]
    public double TotalTatMinutes { get; set; }

    [BsonElement("target_tat_minutes")]
    public double TargetTatMinutes { get; set; }

    [BsonElement("is_compliant")]
    public bool IsCompliant { get; set; }

    [BsonElement("delay_minutes")]
    public double DelayMinutes { get; set; }

    [BsonElement("technician_name")]
    public string? TechnicianName { get; set; }

    [BsonElement("calculated_at")]
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}
