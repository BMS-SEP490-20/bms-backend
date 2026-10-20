using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Individual milestone node in the origin trace visual flow (UC77).
/// </summary>
public class TraceNodeDto
{
    public string NodeType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Badge { get; set; }
    public string BadgeType { get; set; } = "default";
    public DateTime? Timestamp { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Chain of custody metrics and analytical telemetry (UC77).
/// </summary>
public class ChainOfCustodyDto
{
    public string Integrity { get; set; } = "Unbroken";
    public int BarcodeScansCount { get; set; } = 7;
    public string TransportDuration { get; set; } = "12 min (Room 02 → Chem bench)";
    public string AnalyzerModel { get; set; } = "BC-01 · Calibrated 07:30";
    public string ReagentLot { get; set; } = "LOT-7721 · Exp 31 Dec 2026";
    public string QcStatus { get; set; } = "QCF-1142";
    public int RerunCount { get; set; } = 1;
    public int NotesCount { get; set; } = 3;
}

/// <summary>
/// Comprehensive specimen origin and audit trail response (UC77 / Issue I3-088).
/// </summary>
public class SampleOriginTraceDto
{
    public string SpecimenId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public List<TraceNodeDto> Nodes { get; set; } = new();
    public ChainOfCustodyDto Custody { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response payload containing PDF and printable HTML report for specimen origin audit (UC77).
/// </summary>
public class ExportTracePdfResponseDto
{
    public string SpecimenId { get; set; } = string.Empty;
    public string FileDownloadName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public string Base64Data { get; set; } = string.Empty;
    public string HtmlPreview { get; set; } = string.Empty;
    public string VerificationHash { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Item in recent traceable specimens list.
/// </summary>
public class RecentTraceableSpecimenDto
{
    public string SpecimenId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
