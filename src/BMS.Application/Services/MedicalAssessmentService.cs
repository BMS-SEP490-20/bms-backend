using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Clinic;
using BMS.Application.DTOs.MedicalRecords;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <summary>
/// Implementation of Medical Assessment service (UC50 / Iteration 3).
/// Implements clinical data validation, automatic BMI calculation,
/// WHO ICD-10 enforcement, 24-hour legal lock invariant, and encounter association.
/// </summary>
public class MedicalAssessmentService : IMedicalAssessmentService
{
    private readonly IMedicalAssessmentRepository _repository;
    private readonly IPatientRepository? _patientRepository;
    private readonly IAppointmentRepository? _appointmentRepository;
    private readonly BmsDbContext _dbContext;
    private readonly ILogger<MedicalAssessmentService> _logger;

    public MedicalAssessmentService(
        IMedicalAssessmentRepository repository,
        IPatientRepository? patientRepository,
        IAppointmentRepository? appointmentRepository,
        BmsDbContext dbContext,
        ILogger<MedicalAssessmentService> logger)
    {
        _repository = repository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<MedicalAssessmentDto> CreateAsync(
        CreateMedicalAssessmentDto dto,
        string currentUserId,
        string? currentUserName,
        CancellationToken ct = default)
    {
        // 1. Calculate BMI and category
        var (bmi, bmiCategory) = CalculateBmi(dto.Vitals.HeightCm, dto.Vitals.WeightKg);

        // 2. Resolve patient details
        var patientName = dto.PatientName;
        var patientCode = dto.PatientCode;
        var patientDob = dto.PatientDob;
        var patientGender = dto.PatientGender;

        if (string.IsNullOrWhiteSpace(patientName) && _patientRepository != null)
        {
            try
            {
                var patient = await _patientRepository.GetByIdOrCodeAsync(dto.PatientId, ct);
                if (patient != null)
                {
                    patientName = patient.FullName;
                    patientCode ??= patient.PatientCode;
                    patientDob ??= patient.DateOfBirth;
                    patientGender ??= patient.Gender.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve patient details from repository for ID {PatientId}", dto.PatientId);
            }
        }

        // Fallback to in-memory Users if patient is not yet populated
        if (string.IsNullOrWhiteSpace(patientName))
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.Id == dto.PatientId || u.Username == dto.PatientId);
            if (user != null)
            {
                patientName = user.FullName;
                patientCode ??= user.Id;
            }
        }

        patientName ??= $"Bệnh nhân {dto.PatientId}";

        // 3. Resolve doctor details
        var doctorId = !string.IsNullOrWhiteSpace(dto.DoctorId) ? dto.DoctorId : currentUserId;
        var doctorName = dto.DoctorName ?? currentUserName ?? "Bác sĩ lâm sàng";

        // 4. Generate next assessment code
        var assessmentCode = await _repository.GenerateNextAssessmentCodeAsync(DateTime.UtcNow, ct);

        // 5. Create Entity
        var assessment = new MedicalAssessment
        {
            AssessmentCode = assessmentCode,
            PatientId = dto.PatientId,
            PatientCode = patientCode,
            PatientName = patientName,
            PatientDob = patientDob,
            PatientGender = patientGender,
            EncounterId = dto.EncounterId,
            EncounterType = dto.EncounterType ?? "ClinicVisit",
            EncounterDate = dto.AssessmentDate ?? DateTime.UtcNow,
            DoctorId = doctorId,
            DoctorName = doctorName,
            Department = string.IsNullOrWhiteSpace(dto.Department) ? "Khoa KhÃ¡m Bá»‡nh" : dto.Department,
            AssessmentDate = dto.AssessmentDate ?? DateTime.UtcNow,
            ChiefComplaint = dto.ChiefComplaint.Trim(),
            ClinicalSymptoms = dto.ClinicalSymptoms?.Trim(),
            Vitals = new VitalSigns
            {
                HeartRate = dto.Vitals.HeartRate,
                SystolicBp = dto.Vitals.SystolicBp,
                DiastolicBp = dto.Vitals.DiastolicBp,
                Temperature = dto.Vitals.Temperature,
                Spo2 = dto.Vitals.Spo2,
                HeightCm = dto.Vitals.HeightCm,
                WeightKg = dto.Vitals.WeightKg,
                Bmi = bmi,
                BmiCategory = bmiCategory
            },
            PhysicalExamination = dto.PhysicalExamination?.Trim(),
            Icd10Code = dto.Icd10Code.Trim().ToUpperInvariant(),
            Icd10Name = dto.Icd10Name?.Trim(),
            PreliminaryDiagnosis = dto.PreliminaryDiagnosis?.Trim(),
            FinalDiagnosis = dto.FinalDiagnosis.Trim(),
            Severity = string.IsNullOrWhiteSpace(dto.Severity) ? "Mild" : dto.Severity,
            RiskLevel = string.IsNullOrWhiteSpace(dto.RiskLevel) ? "Low" : dto.RiskLevel,
            ClinicalImpression = dto.ClinicalImpression?.Trim(),
            RecommendedTestPackages = dto.RecommendedTestPackages ?? new List<string>(),
            TreatmentPlan = dto.TreatmentPlan?.Trim(),
            PrescriptionNotes = dto.PrescriptionNotes?.Trim(),
            FollowUpDate = dto.FollowUpDate,
            FollowUpNotes = dto.FollowUpNotes?.Trim(),
            Status = string.Equals(dto.Status, "Finalized", StringComparison.OrdinalIgnoreCase) ? "Finalized" : "Draft",
            IsFinalized = string.Equals(dto.Status, "Finalized", StringComparison.OrdinalIgnoreCase),
            FinalizedAt = string.Equals(dto.Status, "Finalized", StringComparison.OrdinalIgnoreCase) ? DateTime.UtcNow : null,
            FinalizedBy = string.Equals(dto.Status, "Finalized", StringComparison.OrdinalIgnoreCase) ? doctorName : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUserId
        };

        await _repository.CreateAsync(assessment, ct);

        _logger.LogInformation("Medical assessment {Code} created for Patient {PatientId} by Doctor {DoctorId}",
            assessment.AssessmentCode, assessment.PatientId, assessment.DoctorId);

        return MapToDto(assessment);
    }

    public async Task<MedicalAssessmentDto?> GetByIdAsync(
        string id,
        string? currentUserId,
        string? currentUserRole,
        CancellationToken ct = default)
    {
        var assessment = await _repository.GetByIdAsync(id, ct);
        if (assessment == null) return null;

        // Security scoping (BR-IAM-02): Patient can only view their own assessments
        if (string.Equals(currentUserRole, "Patient", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(assessment.PatientId, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Unauthorized access attempt by Patient {UserId} to assessment {AssessmentId}", currentUserId, id);
            return null;
        }

        return MapToDto(assessment);
    }

    public async Task<List<MedicalAssessmentDto>> GetByPatientIdAsync(
        string patientId,
        string? currentUserId,
        string? currentUserRole,
        CancellationToken ct = default)
    {
        // Security scoping (BR-IAM-02)
        if (string.Equals(currentUserRole, "Patient", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(patientId, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Unauthorized access attempt by Patient {UserId} to patient {PatientId}", currentUserId, patientId);
            return new List<MedicalAssessmentDto>();
        }

        var list = await _repository.GetByPatientIdAsync(patientId, ct);
        return list.Select(MapToDto).ToList();
    }

    public async Task<List<PatientEncounterDto>> GetPatientEncountersAsync(string patientId, CancellationToken ct = default)
    {
        var encounters = new List<PatientEncounterDto>();

        if (_appointmentRepository != null)
        {
            try
            {
                var appointments = await _appointmentRepository.GetByPatientIdAsync(patientId, ct);
                foreach (var app in appointments)
                {
                    encounters.Add(new PatientEncounterDto
                    {
                        EncounterId = app.AppointmentCode ?? app.Id,
                        EncounterType = app.Type == AppointmentType.HomePhlebotomy ? "HomePhlebotomy" : "ClinicVisit",
                        EncounterDate = app.AppointmentDate.Date.Add(app.StartTime),
                        DoctorId = app.DoctorId,
                        RoomId = app.RoomId,
                        Status = app.Status.ToString(),
                        Notes = app.Notes
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve appointments for patient {PatientId}", patientId);
            }
        }

        // Add default current walk-in encounter if list is empty
        if (encounters.Count == 0)
        {
            encounters.Add(new PatientEncounterDto
            {
                EncounterId = $"ENC-{DateTime.UtcNow:yyMMdd}-001",
                EncounterType = "ClinicVisit",
                EncounterDate = DateTime.UtcNow,
                DoctorId = "DOC-001",
                RoomId = "ROOM-101",
                Status = "InProgress",
                Notes = "Khám lâm sàng tại phòng khám"
            });
        }

        return encounters;
    }

    public async Task<(List<MedicalAssessmentDto> Items, int TotalCount)> QueryAsync(
        MedicalAssessmentFilterDto filter,
        string? currentUserId,
        string? currentUserRole,
        CancellationToken ct = default)
    {
        // Patient role constraint
        var targetPatientId = filter.PatientId;
        if (string.Equals(currentUserRole, "Patient", StringComparison.OrdinalIgnoreCase))
        {
            targetPatientId = currentUserId;
        }

        var skip = (Math.Max(1, filter.PageNumber) - 1) * Math.Max(1, filter.PageSize);
        var limit = Math.Max(1, filter.PageSize);

        var (items, total) = await _repository.QueryAsync(
            patientId: targetPatientId,
            doctorId: filter.DoctorId,
            status: filter.Status,
            fromDate: filter.FromDate,
            toDate: filter.ToDate,
            keyword: filter.Keyword,
            skip: skip,
            limit: limit,
            ct: ct);

        return (items.Select(MapToDto).ToList(), total);
    }

    public async Task<MedicalAssessmentDto> UpdateAsync(
        string id,
        UpdateMedicalAssessmentDto dto,
        string currentUserId,
        string? currentUserName,
        CancellationToken ct = default)
    {
        var assessment = await _repository.GetByIdAsync(id, ct);
        if (assessment == null)
        {
            throw new InvalidOperationException("MSG-ERR-EMR-01: KhÃ´ng tÃ¬m tháº¥y há»“ sÆ¡ Ä‘Ã¡nh giÃ¡ y khoa cáº§n cáº­p nháº­t.");
        }

        if (assessment.IsFinalized)
        {
            throw new InvalidOperationException("MSG-ERR-EMR-03: ÄÃ¡nh giÃ¡ y khoa Ä‘Ã£ hoÃ n táº¥t (Finalized), khÃ´ng thá»ƒ chá»‰nh sá»­a.");
        }

        // 24-hour legal lock invariant
        if (DateTime.UtcNow - assessment.CreatedAt > TimeSpan.FromHours(24))
        {
            _logger.LogWarning("Update rejected: Assessment {Code} is locked (>24 hours old)", assessment.AssessmentCode);
            throw new InvalidOperationException("MSG-ERR-EMR-02: ÄÃ¡nh giÃ¡ y khoa Ä‘Ã£ khÃ³a sau 24 giá» ká»ƒ tá»« thá»i Ä‘iá»ƒm táº¡o, khÃ´ng thá»ƒ chá»‰nh sá»­a.");
        }

        var (bmi, bmiCategory) = CalculateBmi(dto.Vitals.HeightCm, dto.Vitals.WeightKg);

        assessment.ChiefComplaint = dto.ChiefComplaint.Trim();
        assessment.ClinicalSymptoms = dto.ClinicalSymptoms?.Trim();
        assessment.Vitals.HeartRate = dto.Vitals.HeartRate;
        assessment.Vitals.SystolicBp = dto.Vitals.SystolicBp;
        assessment.Vitals.DiastolicBp = dto.Vitals.DiastolicBp;
        assessment.Vitals.Temperature = dto.Vitals.Temperature;
        assessment.Vitals.Spo2 = dto.Vitals.Spo2;
        assessment.Vitals.HeightCm = dto.Vitals.HeightCm;
        assessment.Vitals.WeightKg = dto.Vitals.WeightKg;
        assessment.Vitals.Bmi = bmi;
        assessment.Vitals.BmiCategory = bmiCategory;
        assessment.PhysicalExamination = dto.PhysicalExamination?.Trim();
        assessment.Icd10Code = dto.Icd10Code.Trim().ToUpperInvariant();
        assessment.Icd10Name = dto.Icd10Name?.Trim() ?? assessment.Icd10Name;
        assessment.PreliminaryDiagnosis = dto.PreliminaryDiagnosis?.Trim();
        assessment.FinalDiagnosis = dto.FinalDiagnosis.Trim();
        assessment.Severity = string.IsNullOrWhiteSpace(dto.Severity) ? assessment.Severity : dto.Severity;
        assessment.RiskLevel = string.IsNullOrWhiteSpace(dto.RiskLevel) ? assessment.RiskLevel : dto.RiskLevel;
        assessment.ClinicalImpression = dto.ClinicalImpression?.Trim();
        assessment.RecommendedTestPackages = dto.RecommendedTestPackages ?? assessment.RecommendedTestPackages;
        assessment.TreatmentPlan = dto.TreatmentPlan?.Trim();
        assessment.PrescriptionNotes = dto.PrescriptionNotes?.Trim();
        assessment.FollowUpDate = dto.FollowUpDate;
        assessment.FollowUpNotes = dto.FollowUpNotes?.Trim();
        assessment.UpdatedAt = DateTime.UtcNow;
        assessment.UpdatedBy = currentUserId;

        await _repository.UpdateAsync(assessment, ct);

        _logger.LogInformation("Medical assessment {Code} updated by Doctor {UserId}", assessment.AssessmentCode, currentUserId);
        return MapToDto(assessment);
    }

    public async Task<MedicalAssessmentDto> FinalizeAsync(
        string id,
        FinalizeMedicalAssessmentDto dto,
        string currentUserId,
        string? currentUserName,
        CancellationToken ct = default)
    {
        var assessment = await _repository.GetByIdAsync(id, ct);
        if (assessment == null)
        {
            throw new InvalidOperationException("MSG-ERR-EMR-01: KhÃ´ng tÃ¬m tháº¥y há»“ sÆ¡ Ä‘Ã¡nh giÃ¡ y khoa cáº§n khÃ³a.");
        }

        if (assessment.IsFinalized)
        {
            return MapToDto(assessment);
        }

        assessment.Status = "Finalized";
        assessment.IsFinalized = true;
        assessment.FinalizedAt = DateTime.UtcNow;
        assessment.FinalizedBy = currentUserName ?? currentUserId;
        if (!string.IsNullOrWhiteSpace(dto.FinalNotes))
        {
            assessment.ClinicalImpression = string.IsNullOrWhiteSpace(assessment.ClinicalImpression)
                ? dto.FinalNotes
                : $"{assessment.ClinicalImpression}\n[Ghi chÃº khÃ³a há»“ sÆ¡: {dto.FinalNotes}]";
        }
        assessment.UpdatedAt = DateTime.UtcNow;
        assessment.UpdatedBy = currentUserId;

        await _repository.UpdateAsync(assessment, ct);
        _logger.LogInformation("Medical assessment {Code} finalized by {User}", assessment.AssessmentCode, currentUserId);

        return MapToDto(assessment);
    }

    public async Task<bool> DeleteAsync(
        string id,
        string deleteReason,
        string currentUserId,
        CancellationToken ct = default)
    {
        var assessment = await _repository.GetByIdAsync(id, ct);
        if (assessment == null)
        {
            throw new InvalidOperationException("MSG-ERR-EMR-01: KhÃ´ng tÃ¬m tháº¥y há»“ sÆ¡ Ä‘Ã¡nh giÃ¡ y khoa cáº§n xÃ³a.");
        }

        return await _repository.SoftDeleteAsync(assessment.Id, currentUserId, deleteReason, ct);
    }

    private static (decimal Bmi, string Category) CalculateBmi(decimal heightCm, decimal weightKg)
    {
        if (heightCm <= 0) return (0, "N/A");
        var hMeters = heightCm / 100.0m;
        var bmi = Math.Round(weightKg / (hMeters * hMeters), 2);

        string category;
        if (bmi < 18.5m)
        {
            category = "Thiếu cân (Underweight)";
        }
        else if (bmi <= 22.9m)
        {
            category = "BÃ¬nh thÆ°á»ng (Normal)";
        }
        else if (bmi <= 24.9m)
        {
            category = "Thừa cân (Overweight)";
        }
        else
        {
            category = "BÃ©o phÃ¬ (Obese)";
        }

        return (bmi, category);
    }

    private static MedicalAssessmentDto MapToDto(MedicalAssessment a)
    {
        var canEdit = !a.IsFinalized && (DateTime.UtcNow - a.CreatedAt) <= TimeSpan.FromHours(24);

        return new MedicalAssessmentDto
        {
            Id = a.Id,
            AssessmentCode = a.AssessmentCode,
            PatientId = a.PatientId,
            PatientCode = a.PatientCode,
            PatientName = a.PatientName ?? a.PatientId,
            PatientDob = a.PatientDob,
            PatientGender = a.PatientGender,
            EncounterId = a.EncounterId,
            EncounterType = a.EncounterType,
            EncounterDate = a.EncounterDate,
            DoctorId = a.DoctorId,
            DoctorName = a.DoctorName ?? "Bác sĩ lâm sàng",
            Department = a.Department,
            AssessmentDate = a.AssessmentDate,
            ChiefComplaint = a.ChiefComplaint,
            ClinicalSymptoms = a.ClinicalSymptoms,
            Vitals = new VitalSignsDto
            {
                HeartRate = a.Vitals.HeartRate,
                SystolicBp = a.Vitals.SystolicBp,
                DiastolicBp = a.Vitals.DiastolicBp,
                Temperature = a.Vitals.Temperature,
                Spo2 = a.Vitals.Spo2,
                HeightCm = a.Vitals.HeightCm,
                WeightKg = a.Vitals.WeightKg,
                Bmi = a.Vitals.Bmi,
                BmiCategory = a.Vitals.BmiCategory
            },
            PhysicalExamination = a.PhysicalExamination,
            Icd10Code = a.Icd10Code,
            Icd10Name = a.Icd10Name,
            PreliminaryDiagnosis = a.PreliminaryDiagnosis,
            FinalDiagnosis = a.FinalDiagnosis,
            Severity = a.Severity,
            RiskLevel = a.RiskLevel,
            ClinicalImpression = a.ClinicalImpression,
            RecommendedTestPackages = a.RecommendedTestPackages ?? new List<string>(),
            TreatmentPlan = a.TreatmentPlan,
            PrescriptionNotes = a.PrescriptionNotes,
            FollowUpDate = a.FollowUpDate,
            FollowUpNotes = a.FollowUpNotes,
            Status = a.Status,
            IsFinalized = a.IsFinalized,
            FinalizedAt = a.FinalizedAt,
            FinalizedBy = a.FinalizedBy,
            CanEdit = canEdit,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt
        };
    }
}
