using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using BMS.Application.DTOs.MedicalRecords;

namespace BMS.Application.DTOs.Clinic;

/// <summary>
/// DTO for creating a new Medical Assessment (UC50 / Add Medical Assessment).
/// Validates clinical vital signs, mandatory Chief Complaint, and WHO ICD-10 diagnostic codes.
/// </summary>
public class CreateMedicalAssessmentDto : IValidatableObject
{
    [Required(ErrorMessage = "Mã bệnh nhân (PatientId) là bắt buộc.")]
    public string PatientId { get; set; } = string.Empty;

    public string? PatientCode { get; set; }
    public string? PatientName { get; set; }
    public DateTime? PatientDob { get; set; }
    public string? PatientGender { get; set; }

    public string? EncounterId { get; set; }
    public string EncounterType { get; set; } = "ClinicVisit"; // ClinicVisit, FollowUp, Emergency, RoutineCheckup

    public string? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public string Department { get; set; } = "Khoa Khám Bệnh";

    public DateTime? AssessmentDate { get; set; }

    [Required(ErrorMessage = "Lý do khám (ChiefComplaint) không được để trống.")]
    [StringLength(1000, MinimumLength = 3, ErrorMessage = "Lý do khám phải từ 3 đến 1000 ký tự.")]
    public string ChiefComplaint { get; set; } = string.Empty;

    [MaxLength(2000, ErrorMessage = "Triệu chứng lâm sàng không được vượt quá 2000 ký tự.")]
    public string? ClinicalSymptoms { get; set; }

    [Required(ErrorMessage = "Chỉ số sinh tồn (Vitals) là bắt buộc.")]
    public VitalSignsInputDto Vitals { get; set; } = new();

    [MaxLength(2000, ErrorMessage = "Kết quả khám thực thể không được vượt quá 2000 ký tự.")]
    public string? PhysicalExamination { get; set; }

    [Required(ErrorMessage = "Mã chẩn đoán ICD-10 là bắt buộc theo quy định BR-DOC-01.")]
    public string Icd10Code { get; set; } = string.Empty;

    public string? Icd10Name { get; set; }

    [MaxLength(1000, ErrorMessage = "Chẩn đoán sơ bộ không được vượt quá 1000 ký tự.")]
    public string? PreliminaryDiagnosis { get; set; }

    [Required(ErrorMessage = "Chẩn đoán xác định / Kết luận đánh giá không được để trống.")]
    [MaxLength(1000, ErrorMessage = "Chẩn đoán xác định không được vượt quá 1000 ký tự.")]
    public string FinalDiagnosis { get; set; } = string.Empty;

    public string Severity { get; set; } = "Mild"; // Mild, Moderate, Severe, Critical
    public string RiskLevel { get; set; } = "Low"; // Low, Medium, High

    [MaxLength(2000, ErrorMessage = "Nhận định lâm sàng không được vượt quá 2000 ký tự.")]
    public string? ClinicalImpression { get; set; }

    public List<string> RecommendedTestPackages { get; set; } = new();

    [MaxLength(2000, ErrorMessage = "Kế hoạch điều trị không được vượt quá 2000 ký tự.")]
    public string? TreatmentPlan { get; set; }

    [MaxLength(2000, ErrorMessage = "Ghi chú đơn thuốc không được vượt quá 2000 ký tự.")]
    public string? PrescriptionNotes { get; set; }

    public DateTime? FollowUpDate { get; set; }

    [MaxLength(1000, ErrorMessage = "Dặn dò tái khám không được vượt quá 1000 ký tự.")]
    public string? FollowUpNotes { get; set; }

    public string Status { get; set; } = "Draft"; // Draft, Finalized

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Icd10Code))
        {
            yield return new ValidationResult("Mã chẩn đoán ICD-10 là bắt buộc.", new[] { nameof(Icd10Code) });
        }
        else if (!Regex.IsMatch(Icd10Code.Trim().ToUpperInvariant(), @"^[A-Z][0-9]{2}(\.[0-9]{1,2})?$"))
        {
            yield return new ValidationResult(
                "Mã ICD-10 không đúng định dạng chuẩn WHO (Ví dụ: E11, I10, J00, K76, E11.9).",
                new[] { nameof(Icd10Code) });
        }
    }
}

/// <summary>
/// DTO for updating an existing Medical Assessment (UC50).
/// </summary>
public class UpdateMedicalAssessmentDto : IValidatableObject
{
    [Required(ErrorMessage = "Lý do khám không được để trống.")]
    [StringLength(1000, MinimumLength = 3, ErrorMessage = "Lý do khám phải từ 3 đến 1000 ký tự.")]
    public string ChiefComplaint { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? ClinicalSymptoms { get; set; }

    [Required(ErrorMessage = "Chỉ số sinh tồn là bắt buộc.")]
    public VitalSignsInputDto Vitals { get; set; } = new();

    [MaxLength(2000)]
    public string? PhysicalExamination { get; set; }

    [Required(ErrorMessage = "Mã chẩn đoán ICD-10 là bắt buộc.")]
    public string Icd10Code { get; set; } = string.Empty;

    public string? Icd10Name { get; set; }

    [MaxLength(1000)]
    public string? PreliminaryDiagnosis { get; set; }

    [Required(ErrorMessage = "Chẩn đoán xác định không được để trống.")]
    [MaxLength(1000)]
    public string FinalDiagnosis { get; set; } = string.Empty;

    public string Severity { get; set; } = "Mild";
    public string RiskLevel { get; set; } = "Low";

    [MaxLength(2000)]
    public string? ClinicalImpression { get; set; }

    public List<string> RecommendedTestPackages { get; set; } = new();

    [MaxLength(2000)]
    public string? TreatmentPlan { get; set; }

    [MaxLength(2000)]
    public string? PrescriptionNotes { get; set; }

    public DateTime? FollowUpDate { get; set; }

    [MaxLength(1000)]
    public string? FollowUpNotes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Icd10Code))
        {
            yield return new ValidationResult("Mã chẩn đoán ICD-10 là bắt buộc.", new[] { nameof(Icd10Code) });
        }
        else if (!Regex.IsMatch(Icd10Code.Trim().ToUpperInvariant(), @"^[A-Z][0-9]{2}(\.[0-9]{1,2})?$"))
        {
            yield return new ValidationResult(
                "Mã ICD-10 không đúng định dạng chuẩn WHO (Ví dụ: E11, I10, J00).",
                new[] { nameof(Icd10Code) });
        }
    }
}

/// <summary>
/// Request for finalizing/locking a medical assessment.
/// </summary>
public class FinalizeMedicalAssessmentDto
{
    [MaxLength(500)]
    public string? FinalNotes { get; set; }
}

/// <summary>
/// Query filter parameters for medical assessment listing.
/// </summary>
public class MedicalAssessmentFilterDto
{
    public string? PatientId { get; set; }
    public string? DoctorId { get; set; }
    public string? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Keyword { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Clinical representation of an encounter / appointment for clinical association.
/// </summary>
public class PatientEncounterDto
{
    public string EncounterId { get; set; } = string.Empty;
    public string EncounterType { get; set; } = "ClinicVisit";
    public DateTime EncounterDate { get; set; }
    public string? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public string? RoomId { get; set; }
    public string? Status { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Complete medical assessment response payload (UC50).
/// </summary>
public class MedicalAssessmentDto
{
    public string Id { get; set; } = string.Empty;
    public string AssessmentCode { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string? PatientCode { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public DateTime? PatientDob { get; set; }
    public string? PatientGender { get; set; }
    public string? EncounterId { get; set; }
    public string EncounterType { get; set; } = string.Empty;
    public DateTime? EncounterDate { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime AssessmentDate { get; set; }
    public string ChiefComplaint { get; set; } = string.Empty;
    public string? ClinicalSymptoms { get; set; }
    public VitalSignsDto Vitals { get; set; } = new();
    public string? PhysicalExamination { get; set; }
    public string Icd10Code { get; set; } = string.Empty;
    public string? Icd10Name { get; set; }
    public string? PreliminaryDiagnosis { get; set; }
    public string FinalDiagnosis { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string? ClinicalImpression { get; set; }
    public List<string> RecommendedTestPackages { get; set; } = new();
    public string? TreatmentPlan { get; set; }
    public string? PrescriptionNotes { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public string? FollowUpNotes { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public string? FinalizedBy { get; set; }
    public bool CanEdit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
