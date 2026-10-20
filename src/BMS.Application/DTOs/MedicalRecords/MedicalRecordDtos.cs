using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BMS.Application.DTOs.MedicalRecords;

public class VitalSignsInputDto : IValidatableObject
{
    [Range(30, 250, ErrorMessage = "Mạch phải từ 30 đến 250 bpm.")]
    public int HeartRate { get; set; } = 75;

    [Range(60, 260, ErrorMessage = "Huyết áp tâm thu phải từ 60 đến 260 mmHg.")]
    public int SystolicBp { get; set; } = 120;

    [Range(40, 150, ErrorMessage = "Huyết áp tâm trương phải từ 40 đến 150 mmHg.")]
    public int DiastolicBp { get; set; } = 80;

    [Range(34.0, 43.0, ErrorMessage = "Thân nhiệt phải từ 34.0 đến 43.0 độ C.")]
    public decimal Temperature { get; set; } = 36.5m;

    [Range(50, 100, ErrorMessage = "Chỉ số SpO2 phải từ 50 đến 100%.")]
    public int Spo2 { get; set; } = 98;

    [Range(20.0, 250.0, ErrorMessage = "Chiều cao phải từ 20 đến 250 cm.")]
    public decimal HeightCm { get; set; } = 170.0m;

    [Range(1.0, 300.0, ErrorMessage = "Cân nặng phải từ 1.0 đến 300.0 kg.")]
    public decimal WeightKg { get; set; } = 65.0m;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SystolicBp <= DiastolicBp + 15)
        {
            yield return new ValidationResult(
                "Huyết áp tâm thu phải lớn hơn huyết áp tâm trương ít nhất 15 mmHg.",
                new[] { nameof(SystolicBp), nameof(DiastolicBp) });
        }
    }
}

public class CreateMedicalRecordDto : IValidatableObject
{
    [Required(ErrorMessage = "PatientId là bắt buộc.")]
    public string PatientId { get; set; } = string.Empty;

    public string? DoctorId { get; set; }
    public string? AppointmentId { get; set; }

    [Required(ErrorMessage = "Lý do khám (ChiefComplaint) không được để trống.")]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = "Lý do khám phải từ 5 đến 1000 ký tự.")]
    public string ChiefComplaint { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? ClinicalSymptoms { get; set; }

    [Required(ErrorMessage = "Dấu hiệu sinh tồn là bắt buộc.")]
    public VitalSignsInputDto Vitals { get; set; } = new();

    [Required(ErrorMessage = "Mã chẩn đoán ICD-10 là bắt buộc.")]
    public string Icd10Code { get; set; } = string.Empty;

    public string? Icd10Name { get; set; }

    [Required(ErrorMessage = "Chẩn đoán xác định không được để trống.")]
    [MaxLength(1000)]
    public string Diagnosis { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? TreatmentPlan { get; set; }

    [MaxLength(2000)]
    public string? PrescriptionNotes { get; set; }

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

public class UpdateMedicalRecordDto : IValidatableObject
{
    [Required(ErrorMessage = "Lý do khám không được để trống.")]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = "Lý do khám phải từ 5 đến 1000 ký tự.")]
    public string ChiefComplaint { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? ClinicalSymptoms { get; set; }

    [Required(ErrorMessage = "Dấu hiệu sinh tồn là bắt buộc.")]
    public VitalSignsInputDto Vitals { get; set; } = new();

    [Required(ErrorMessage = "Mã chẩn đoán ICD-10 là bắt buộc.")]
    public string Icd10Code { get; set; } = string.Empty;

    public string? Icd10Name { get; set; }

    [Required(ErrorMessage = "Chẩn đoán xác định không được để trống.")]
    [MaxLength(1000)]
    public string Diagnosis { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? TreatmentPlan { get; set; }

    [MaxLength(2000)]
    public string? PrescriptionNotes { get; set; }

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

public class DeleteMedicalRecordRequest
{
    [Required(ErrorMessage = "Lý do hủy/xóa bệnh án là bắt buộc.")]
    [StringLength(500, MinimumLength = 15, ErrorMessage = "Lý do xóa phải có ít nhất 15 ký tự và tối đa 500 ký tự giải trình kiểm toán.")]
    public string DeleteReason { get; set; } = string.Empty;
}

public class MedicalRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string RecordCode { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string? AppointmentId { get; set; }
    public string ChiefComplaint { get; set; } = string.Empty;
    public string? ClinicalSymptoms { get; set; }
    public VitalSignsDto Vitals { get; set; } = new();
    public string Icd10Code { get; set; } = string.Empty;
    public string? Icd10Name { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string? TreatmentPlan { get; set; }
    public string? PrescriptionNotes { get; set; }
    public bool CanEdit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class VitalSignsDto
{
    public int HeartRate { get; set; }
    public int SystolicBp { get; set; }
    public int DiastolicBp { get; set; }
    public decimal Temperature { get; set; }
    public int Spo2 { get; set; }
    public decimal HeightCm { get; set; }
    public decimal WeightKg { get; set; }
    public decimal Bmi { get; set; }
    public string BmiCategory { get; set; } = string.Empty;
}
