using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Multi-rule Westgard evaluation status for an IQC run (ISO 15189 / CLSI C24-A3).
/// </summary>
public enum WestgardStatus
{
    PASS = 1,              // In control (|Z| <= 2)
    WARNING_1_2S = 2,      // Warning: 1 control observation exceeds +/- 2s
    REJECT_1_3S = 3,       // Rejection: 1 control observation exceeds +/- 3s (Random error)
    REJECT_2_2S = 4,       // Rejection: 2 consecutive observations exceed +/- 2s on same side (Systematic error)
    REJECT_R_4S = 5,       // Rejection: Range of 2 observations in run >= 4s (Random error)
    REJECT_4_1S = 6,       // Rejection: 4 consecutive observations exceed +/- 1s on same side (Systematic error)
    REJECT_10_X = 7        // Rejection: 10 consecutive observations on same side of mean (Systematic drift)
}

/// <summary>
/// Control Material Lot definition for Internal Quality Control (UC73).
/// Maps to MongoDB collection "qc_lots".
/// </summary>
[BsonIgnoreExtraElements]
public class QcLot
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("lot_number")]
    public string LotNumber { get; set; } = string.Empty; // e.g. LOT-GLU-2026A

    [BsonElement("control_name")]
    public string ControlName { get; set; } = string.Empty; // e.g. Bio-Rad Liquid Assayed Multiqual

    [BsonElement("analyte_name")]
    public string AnalyteName { get; set; } = string.Empty; // e.g. Glucose, Creatinine, AST, ALT

    [BsonElement("instrument_id")]
    public string InstrumentId { get; set; } = string.Empty; // e.g. ROCHE-COBAS-c501

    [BsonElement("manufacturer")]
    public string Manufacturer { get; set; } = "Bio-Rad Laboratories";

    [BsonElement("level")]
    public int Level { get; set; } = 1; // 1 = Normal, 2 = High, 3 = Low

    [BsonElement("target_mean")]
    public double TargetMean { get; set; } // Expected target value (mu)

    [BsonElement("target_sd")]
    public double TargetStandardDeviation { get; set; } // Expected standard deviation (sigma)

    [BsonElement("unit")]
    public string Unit { get; set; } = "mmol/L";

    [BsonElement("expiration_date")]
    public DateTime ExpirationDate { get; set; } = DateTime.UtcNow.AddMonths(12);

    [BsonElement("is_active")]
    public bool IsActive { get; set; } = true;

    [BsonElement("created_date")]
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [BsonElement("created_by")]
    public string CreatedBy { get; set; } = "KTV Trưởng Lab";
}

/// <summary>
/// Recorded IQC Run measurement with Westgard multi-rule evaluation (UC73).
/// Maps to MongoDB collection "qc_runs".
/// </summary>
[BsonIgnoreExtraElements]
public class QcRun
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("lot_id")]
    public string LotId { get; set; } = string.Empty;

    [BsonElement("run_number")]
    public int RunNumber { get; set; }

    [BsonElement("measured_value")]
    public double MeasuredValue { get; set; }

    [BsonElement("z_score")]
    public double ZScore { get; set; }

    [BsonElement("evaluated_at")]
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("operator_name")]
    public string OperatorName { get; set; } = string.Empty;

    [BsonElement("instrument_id")]
    public string InstrumentId { get; set; } = string.Empty;

    [BsonElement("status")]
    public string StatusString { get; set; } = WestgardStatus.PASS.ToString();

    [BsonIgnore]
    public WestgardStatus Status
    {
        get => Enum.TryParse<WestgardStatus>(StatusString, true, out var s) ? s : WestgardStatus.PASS;
        set => StatusString = value.ToString();
    }

    [BsonElement("violated_rules")]
    public List<string> ViolatedRules { get; set; } = new();

    [BsonElement("is_locked")]
    public bool IsLocked { get; set; } // If true, analyzer batch is locked from releasing patient results

    [BsonElement("corrective_action")]
    public string? CorrectiveAction { get; set; }

    [BsonElement("resolved_at")]
    public DateTime? ResolvedAt { get; set; }

    [BsonElement("resolved_by")]
    public string? ResolvedBy { get; set; }
}
