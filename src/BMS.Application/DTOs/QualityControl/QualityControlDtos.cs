using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.QualityControl;

/// <summary>
/// Data Transfer Object for QC Control Lot metadata (UC73).
/// </summary>
public class QcLotDto
{
    public string Id { get; set; } = string.Empty;
    public string LotNumber { get; set; } = string.Empty;
    public string ControlName { get; set; } = string.Empty;
    public string AnalyteName { get; set; } = string.Empty;
    public string InstrumentId { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public int Level { get; set; }
    public double TargetMean { get; set; }
    public double TargetStandardDeviation { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime ExpirationDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public int TotalRunsCount { get; set; }
    public bool IsInControl { get; set; } = true;
}

/// <summary>
/// Payload for creating a new QC Control Lot.
/// </summary>
public class CreateQcLotDto
{
    public string LotNumber { get; set; } = string.Empty;
    public string ControlName { get; set; } = string.Empty;
    public string AnalyteName { get; set; } = string.Empty;
    public string InstrumentId { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = "Bio-Rad Laboratories";
    public int Level { get; set; } = 1; // 1 = Normal, 2 = High, 3 = Low
    public double TargetMean { get; set; }
    public double TargetStandardDeviation { get; set; }
    public string Unit { get; set; } = "mmol/L";
    public DateTime ExpirationDate { get; set; } = DateTime.UtcNow.AddMonths(12);
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Payload for updating an existing QC Lot.
/// </summary>
public class UpdateQcLotDto
{
    public string? ControlName { get; set; }
    public double? TargetMean { get; set; }
    public double? TargetStandardDeviation { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>
/// Payload for recording an analytical IQC measurement run.
/// </summary>
public class RecordQcRunDto
{
    public string LotId { get; set; } = string.Empty;
    public double MeasuredValue { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string? InstrumentId { get; set; }
    public DateTime? Timestamp { get; set; }
}

/// <summary>
/// Result of evaluating an IQC measurement against Westgard Multi-Rules.
/// </summary>
public class QcEvaluationResultDto
{
    public string RunId { get; set; } = string.Empty;
    public string LotId { get; set; } = string.Empty;
    public int RunNumber { get; set; }
    public double MeasuredValue { get; set; }
    public double ZScore { get; set; }
    public string Status { get; set; } = "PASS";
    public List<string> ViolatedRules { get; set; } = new();
    public bool IsLocked { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime EvaluatedAt { get; set; }
}

/// <summary>
/// Individual data point on the Levey-Jennings QC chart.
/// </summary>
public class LeveyJenningsDataPointDto
{
    public string RunId { get; set; } = string.Empty;
    public int RunNumber { get; set; }
    public DateTime Timestamp { get; set; }
    public double MeasuredValue { get; set; }
    public double ZScore { get; set; }
    public string Status { get; set; } = "PASS";
    public List<string> ViolatedRules { get; set; } = new();
    public bool IsLocked { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string? CorrectiveAction { get; set; }
}

/// <summary>
/// Full Levey-Jennings Chart dataset for visual QC review.
/// </summary>
public class LeveyJenningsChartDto
{
    public QcLotDto Lot { get; set; } = new();
    public double Mean { get; set; }
    public double SD { get; set; }
    public double Plus1SD { get; set; }
    public double Plus2SD { get; set; }
    public double Plus3SD { get; set; }
    public double Minus1SD { get; set; }
    public double Minus2SD { get; set; }
    public double Minus3SD { get; set; }
    public List<LeveyJenningsDataPointDto> Points { get; set; } = new();
    public bool IsInControl { get; set; } = true;
    public int TotalRuns { get; set; }
    public int WarningCount { get; set; }
    public int RejectionCount { get; set; }
}

/// <summary>
/// Request payload to sign off corrective action for an out-of-control QC run.
/// </summary>
public class QcSignoffRequestDto
{
    public string OperatorName { get; set; } = string.Empty;
    public string CorrectiveAction { get; set; } = string.Empty;
}

/// <summary>
/// Summary KPI dashboard for laboratory QC surveillance.
/// </summary>
public class QcDashboardSummaryDto
{
    public int ActiveLotsCount { get; set; }
    public int RunsTodayCount { get; set; }
    public double InControlRate { get; set; }
    public int ActiveWarningsCount { get; set; }
    public int ActiveRejectionsCount { get; set; }
    public int LockedAnalyzersCount { get; set; }
}
