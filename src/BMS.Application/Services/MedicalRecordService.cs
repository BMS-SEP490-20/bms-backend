using BMS.Application.DTOs.MedicalRecords;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Services;

/// <summary>
/// UC33 – Medical Records (EMR) Management Service.
/// Implements clinical BMI calculation, WHO ICD-10 code enforcement,
/// 24-hour legal lock invariant, and audit-compliant soft deletion.
/// </summary>
public class MedicalRecordService : IMedicalRecordService
{
    private readonly IMedicalRecordRepository _repository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ILogger<MedicalRecordService> _logger;

    public MedicalRecordService(
        IMedicalRecordRepository repository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        ILogger<MedicalRecordService> logger)
    {
        _repository = repository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _logger = logger;
    }

    public async Task<List<MedicalRecordDto>> GetByPatientIdAsync(
        string patientId,
        string? currentUserId,
        string? currentUserRole,
        CancellationToken ct = default)
    {
        // Security scoping (BR-IAM-02): Patient can only view their own records
        if (string.Equals(currentUserRole, "Patient", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(patientId, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Patient {UserId} attempted unauthorized access to EMR of patient {TargetId}", currentUserId, patientId);
            return new List<MedicalRecordDto>();
        }

        var records = await _repository.GetByPatientIdAsync(patientId, ct);
        var patient = await _patientRepository.GetByIdOrCodeAsync(patientId, ct);
        var patientName = patient?.FullName ?? patientId;

        var doctorIds = records.Select(r => r.DoctorId).Distinct().ToList();
        var doctors = (await _appointmentRepository.GetDoctorsByIdsAsync(doctorIds, ct)).ToDictionary(d => d.Id);

        return records.Select(r =>
        {
            doctors.TryGetValue(r.DoctorId, out var doc);
            return MapToDto(r, patientName, doc?.FullName);
        }).ToList();
    }

    public async Task<MedicalRecordDto?> GetByIdAsync(
        string id,
        string? currentUserId,
        string? currentUserRole,
        CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        if (record == null) return null;

        // Security scoping (BR-IAM-02)
        if (string.Equals(currentUserRole, "Patient", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(record.PatientId, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var patient = await _patientRepository.GetByIdOrCodeAsync(record.PatientId, ct);
        var doctor = await _appointmentRepository.GetDoctorAsync(record.DoctorId, ct);

        return MapToDto(record, patient?.FullName ?? record.PatientId, doctor?.FullName);
    }

    public async Task<MedicalRecordDto> CreateAsync(
        CreateMedicalRecordDto dto,
        string doctorUserId,
        CancellationToken ct = default)
    {
        var (bmi, bmiCategory) = CalculateBmi(dto.Vitals.HeightCm, dto.Vitals.WeightKg);
        var recordCode = await _repository.GenerateNextRecordCodeAsync(DateTime.UtcNow, ct);

        var record = new MedicalRecord
        {
            RecordCode = recordCode,
            PatientId = dto.PatientId,
            DoctorId = !string.IsNullOrWhiteSpace(dto.DoctorId) ? dto.DoctorId : doctorUserId,
            AppointmentId = dto.AppointmentId,
            ChiefComplaint = dto.ChiefComplaint,
            ClinicalSymptoms = dto.ClinicalSymptoms,
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
            Icd10Code = dto.Icd10Code.Trim().ToUpperInvariant(),
            Icd10Name = dto.Icd10Name,
            Diagnosis = dto.Diagnosis,
            TreatmentPlan = dto.TreatmentPlan,
            PrescriptionNotes = dto.PrescriptionNotes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.InsertAsync(record, ct);

        _logger.LogInformation("Medical record {Code} created for Patient {PatientId} by Doctor {DoctorId}",
            record.RecordCode, record.PatientId, record.DoctorId);

        var patient = await _patientRepository.GetByIdOrCodeAsync(record.PatientId, ct);
        var doctor = await _appointmentRepository.GetDoctorAsync(record.DoctorId, ct);

        return MapToDto(record, patient?.FullName ?? record.PatientId, doctor?.FullName);
    }

    public async Task<MedicalRecordDto> UpdateAsync(
        string id,
        UpdateMedicalRecordDto dto,
        string doctorUserId,
        CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        if (record == null)
        {
            throw new InvalidOperationException("MSG-ERR-EMR-01: Không tìm thấy hồ sơ bệnh án cần cập nhật.");
        }

        // 24-hour legal lock invariant
        if (DateTime.UtcNow - record.CreatedAt > TimeSpan.FromHours(24))
        {
            _logger.LogWarning("Update rejected: Record {Code} is older than 24 hours (Created at {CreatedAt})",
                record.RecordCode, record.CreatedAt);
            throw new InvalidOperationException("MSG-ERR-EMR-02: Hồ sơ bệnh án đã khóa sau 24 giờ kể từ thời điểm tạo, không thể chỉnh sửa.");
        }

        var (bmi, bmiCategory) = CalculateBmi(dto.Vitals.HeightCm, dto.Vitals.WeightKg);

        record.ChiefComplaint = dto.ChiefComplaint;
        record.ClinicalSymptoms = dto.ClinicalSymptoms;
        record.Vitals.HeartRate = dto.Vitals.HeartRate;
        record.Vitals.SystolicBp = dto.Vitals.SystolicBp;
        record.Vitals.DiastolicBp = dto.Vitals.DiastolicBp;
        record.Vitals.Temperature = dto.Vitals.Temperature;
        record.Vitals.Spo2 = dto.Vitals.Spo2;
        record.Vitals.HeightCm = dto.Vitals.HeightCm;
        record.Vitals.WeightKg = dto.Vitals.WeightKg;
        record.Vitals.Bmi = bmi;
        record.Vitals.BmiCategory = bmiCategory;
        record.Icd10Code = dto.Icd10Code.Trim().ToUpperInvariant();
        record.Icd10Name = dto.Icd10Name ?? record.Icd10Name;
        record.Diagnosis = dto.Diagnosis;
        record.TreatmentPlan = dto.TreatmentPlan;
        record.PrescriptionNotes = dto.PrescriptionNotes;
        record.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(record, ct);

        _logger.LogInformation("Medical record {Code} updated by Doctor {DoctorId}", record.RecordCode, doctorUserId);

        var patient = await _patientRepository.GetByIdOrCodeAsync(record.PatientId, ct);
        var doctor = await _appointmentRepository.GetDoctorAsync(record.DoctorId, ct);

        return MapToDto(record, patient?.FullName ?? record.PatientId, doctor?.FullName);
    }

    public async Task<bool> DeleteAsync(
        string id,
        DeleteMedicalRecordRequest request,
        string doctorUserId,
        CancellationToken ct = default)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        if (record == null)
        {
            throw new InvalidOperationException("MSG-ERR-EMR-01: Không tìm thấy hồ sơ bệnh án cần xóa.");
        }

        var success = await _repository.SoftDeleteAsync(record.Id, doctorUserId, request.DeleteReason, ct);

        _logger.LogInformation("Medical record {Code} soft-deleted by {User}. Reason: {Reason}",
            record.RecordCode, doctorUserId, request.DeleteReason);

        return success;
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
            category = "Bình thường (Normal)";
        }
        else if (bmi <= 24.9m)
        {
            category = "Thừa cân (Overweight)";
        }
        else
        {
            category = "Béo phì (Obese)";
        }

        return (bmi, category);
    }

    private static MedicalRecordDto MapToDto(MedicalRecord r, string patientName, string? doctorName)
    {
        var canEdit = (DateTime.UtcNow - r.CreatedAt) <= TimeSpan.FromHours(24);

        return new MedicalRecordDto
        {
            Id = r.Id,
            RecordCode = r.RecordCode,
            PatientId = r.PatientId,
            PatientName = patientName,
            DoctorId = r.DoctorId,
            DoctorName = doctorName ?? "Bác sĩ điều trị",
            AppointmentId = r.AppointmentId,
            ChiefComplaint = r.ChiefComplaint,
            ClinicalSymptoms = r.ClinicalSymptoms,
            Vitals = new VitalSignsDto
            {
                HeartRate = r.Vitals.HeartRate,
                SystolicBp = r.Vitals.SystolicBp,
                DiastolicBp = r.Vitals.DiastolicBp,
                Temperature = r.Vitals.Temperature,
                Spo2 = r.Vitals.Spo2,
                HeightCm = r.Vitals.HeightCm,
                WeightKg = r.Vitals.WeightKg,
                Bmi = r.Vitals.Bmi,
                BmiCategory = r.Vitals.BmiCategory
            },
            Icd10Code = r.Icd10Code,
            Icd10Name = r.Icd10Name,
            Diagnosis = r.Diagnosis,
            TreatmentPlan = r.TreatmentPlan,
            PrescriptionNotes = r.PrescriptionNotes,
            CanEdit = canEdit,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }
}
