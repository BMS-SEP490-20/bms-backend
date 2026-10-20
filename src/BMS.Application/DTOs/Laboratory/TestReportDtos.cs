using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Parameter result line item inside clinical test report (UC42).
/// </summary>
public class TestReportParameterDto
{
    public string ParameterName { get; set; } = string.Empty;
    public string ResultValue { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string ReferenceRange { get; set; } = string.Empty;
    public string Flag { get; set; } = string.Empty; // "", "H", "L", "CRITICAL"
    public bool IsAbnormal { get; set; }
}

/// <summary>
/// Detailed diagnostic laboratory report DTO (UC42).
/// </summary>
public class TestReportDto
{
    public string Id { get; set; } = string.Empty;
    public string ReportId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DobAgeSex { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string CollectionTime { get; set; } = string.Empty;
    public string SpecimenId { get; set; } = string.Empty;
    public string ReferringDoctor { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<TestReportParameterDto> Parameters { get; set; } = new();
    public string ClinicalNotes { get; set; } = string.Empty;
    public string AnalysedBy { get; set; } = string.Empty;
    public DateTime AnalysedAt { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime ApprovedAt { get; set; }
    public DateTime IssuedAt { get; set; }
    public string VerificationHash { get; set; } = string.Empty;
}

/// <summary>
/// Requisition payload to generate or sign an official diagnostic report.
/// </summary>
public class GenerateTestReportRequestDto
{
    public string OrderId { get; set; } = string.Empty;
    public string? ClinicalNotes { get; set; }
    public string? ApprovedBy { get; set; }
}

/// <summary>
/// PDF report export metadata and printable HTML payload.
/// </summary>
public class ExportReportPdfResponseDto
{
    public string ReportId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/pdf";
    public string VerificationHash { get; set; } = string.Empty;
    public string PrintableHtml { get; set; } = string.Empty;
}
