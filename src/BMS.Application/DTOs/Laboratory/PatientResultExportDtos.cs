using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Individual test parameter result for patient portal test result display and PDF export (UC62).
/// </summary>
public class PatientResultParameterDto
{
    public string ParameterName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string ReferenceRange { get; set; } = string.Empty;
    public string? DeltaPrevious { get; set; } // e.g. "+0.42 ↑", "-0.12", "—"
    public string Flag { get; set; } = "N"; // "HH", "H", "N", "L", "LL"
    public bool IsAbnormal { get; set; }
    public string FlagSeverity { get; set; } = "normal"; // "critical", "warning", "normal"
}

/// <summary>
/// Patient-facing test result detail DTO (UC62 / Issue I3-043).
/// </summary>
public class PatientTestResultDetailDto
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public string? AppointmentCode { get; set; }
    public string? Barcode { get; set; }
    public string? SampleType { get; set; }
    public string? LabRoom { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string Category { get; set; } = "Xét Nghiệm Lâm Sàng";
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DobAgeSex { get; set; } = string.Empty;
    public string CollectionTime { get; set; } = string.Empty;
    public string ReleasedDate { get; set; } = string.Empty;
    public string? OrderingDoctor { get; set; }
    public string? PerformedBy { get; set; }
    public string ApprovedByDoctor { get; set; } = string.Empty;
    public string DoctorSpecialty { get; set; } = "Chuyên khoa Lâm sàng";
    public string? Icd10Code { get; set; }
    public string? FinalDiagnosis { get; set; }
    public string DoctorAssessment { get; set; } = string.Empty;
    public string? TreatmentAdvice { get; set; }
    public string? FollowUpDate { get; set; }
    public string OverallStatus { get; set; } = "Released";
    public int AbnormalCount { get; set; }
    public string ReferenceIntervalNotes { get; set; } = "Khoảng tham chiếu sinh học theo chuẩn ISO 15189:2022";
    public List<PatientResultParameterDto> Parameters { get; set; } = new();
    public string VerificationHash { get; set; } = string.Empty;
    public string ShareToken { get; set; } = string.Empty;
    public string ShareUrl { get; set; } = string.Empty;
}

/// <summary>
/// Response payload containing PDF document metadata and printable HTML rendering for export (UC62).
/// </summary>
public class ExportPatientPdfResponseDto
{
    public string OrderId { get; set; } = string.Empty;
    public string ReportId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/pdf";
    public string VerificationHash { get; set; } = string.Empty;
    public string PrintableHtml { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response payload for generating secure shareable patient result link (UC62).
/// </summary>
public class ShareResultResponseDto
{
    public string OrderId { get; set; } = string.Empty;
    public string ShareToken { get; set; } = string.Empty;
    public string ShareUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = "Active";
}
