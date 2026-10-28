using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.Statistics;

/// <summary>
/// Query filter parameters for requesting laboratory Turnaround Time (TAT) statistics (UC63).
/// </summary>
public class TatQueryParameters
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Department { get; set; } // "Biochemistry", "Hematology", "Immunology", etc.
    public string? Priority { get; set; }   // "Stat", "Urgent", "Normal", or "All"
    public string? LabRoom { get; set; }
    public int TargetStatMinutes { get; set; } = 60;
    public int TargetUrgentMinutes { get; set; } = 120;
    public int TargetNormalMinutes { get; set; } = 240;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Top-level key performance indicators for TAT.
/// </summary>
public class TatSummaryStatisticsDto
{
    public int TotalCompletedTests { get; set; }
    public int TotalPendingTests { get; set; }
    public int TotalRejectedCount { get; set; }
    public double AverageTatMinutes { get; set; }
    public string AverageTatFormatted { get; set; } = "0m";
    public double MedianTatMinutes { get; set; }
    public string MedianTatFormatted { get; set; } = "0m";
    public double P90TatMinutes { get; set; }
    public string P90TatFormatted { get; set; } = "0m";
    public double MinTatMinutes { get; set; }
    public double MaxTatMinutes { get; set; }
    public double TargetComplianceRate { get; set; } // e.g. 94.5%
    public int ViolationCount { get; set; }
}

/// <summary>
/// TAT metrics categorized by urgency priority.
/// </summary>
public class TatPriorityBreakdownDto
{
    public string Priority { get; set; } = string.Empty; // "Stat", "Urgent", "Normal"
    public int TotalCount { get; set; }
    public double TargetMinutes { get; set; }
    public double AverageTatMinutes { get; set; }
    public string AverageTatFormatted { get; set; } = "0m";
    public double ComplianceRate { get; set; }
    public int ViolationCount { get; set; }
}

/// <summary>
/// TAT metrics categorized by clinical laboratory department / category.
/// </summary>
public class TatDepartmentBreakdownDto
{
    public string Department { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public double AverageTatMinutes { get; set; }
    public string AverageTatFormatted { get; set; } = "0m";
    public double ComplianceRate { get; set; }
    public int ViolationCount { get; set; }
}

/// <summary>
/// Breakdown of turnaround time across the 3 core laboratory phases (ISO 15189).
/// </summary>
public class TatPhaseBreakdownDto
{
    public string PhaseName { get; set; } = string.Empty; // "PreAnalytical", "Analytical", "PostAnalytical"
    public string PhaseDescription { get; set; } = string.Empty;
    public double AverageMinutes { get; set; }
    public string AverageFormatted { get; set; } = "0m";
    public double PercentageOfTotal { get; set; } // e.g. 25.0%
}

/// <summary>
/// Single point for daily/hourly turnaround time trend charts.
/// </summary>
public class TatTrendPointDto
{
    public string DateLabel { get; set; } = string.Empty; // "2026-10-22"
    public int Volume { get; set; }
    public double AverageTatMinutes { get; set; }
    public double TargetTatMinutes { get; set; }
    public double ComplianceRate { get; set; }
    public int ViolationCount { get; set; }
}

/// <summary>
/// Granular violation item for samples exceeding benchmark target TAT.
/// </summary>
public class TatViolationDetailDto
{
    public string SampleId { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string TestPackage { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public double ActualTatMinutes { get; set; }
    public double TargetTatMinutes { get; set; }
    public double DelayMinutes { get; set; }
    public string BottleneckPhase { get; set; } = "Analytical";
    public string? TechnicianName { get; set; }
}

/// <summary>
/// Comprehensive Turnaround Time (TAT) Analytics Master Report (UC63).
/// </summary>
public class TatStatisticsReportDto
{
    public DateTime QueryFromDate { get; set; }
    public DateTime QueryToDate { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public TatSummaryStatisticsDto Summary { get; set; } = new();
    public List<TatPriorityBreakdownDto> PriorityBreakdown { get; set; } = new();
    public List<TatDepartmentBreakdownDto> DepartmentBreakdown { get; set; } = new();
    public List<TatPhaseBreakdownDto> PhaseBreakdown { get; set; } = new();
    public List<TatTrendPointDto> Trends { get; set; } = new();
    public List<TatViolationDetailDto> RecentViolations { get; set; } = new();
}
