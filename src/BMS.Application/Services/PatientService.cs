using BMS.Core.Utilities;
using BMS.Core.Constants;
using System.Security.Cryptography;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Patients;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <summary>
/// Application service implementing Create Guest Profile and View Patient Information use cases.
/// </summary>
public sealed class PatientService(
    IPatientRepository patientRepository,
    IUserRepository userRepository,
    ISequenceGenerator sequences,
    TimeProvider clock,
    ILogger<PatientService> logger) : IPatientService
{
    /// <summary>
    /// UC25 - Create Guest Profile (Issue I2-016 | BR-077 / BR-REC-01).
    /// Creates a guest/walk-in patient profile with CCCD duplicate prevention,
    /// demographic collision checks, and standard PAT-xxx code generation.
    /// </summary>
    public async Task<PatientInformationDto> CreateGuestProfileAsync(
        CreateGuestProfileRequest request,
        string? createdByUserId,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var fullName = request.FullName.Trim();
        var phone = request.PhoneNumber.Trim();
        var identityNumber = string.IsNullOrWhiteSpace(request.IdentityNumber)
            ? null
            : request.IdentityNumber.Trim();

        PatientIdentityRules.Validate(identityNumber, phone);

        // 1. Check duplicate identity card number (CCCD) — MSG-ERR-REC-01. WHY masked: the message reaches the
        // UI and logs; it must not echo the full CCCD or another patient's name (INV-4).
        if (!string.IsNullOrWhiteSpace(identityNumber))
        {
            var existingByIdCard = await patientRepository.FindByIdentityNumberAsync(identityNumber, ct);
            if (existingByIdCard != null)
            {
                throw new PatientDuplicateException(
                    "MSG-ERR-REC-01",
                    $"Số CCCD {PiiMasker.Mask(identityNumber)} đã có hồ sơ (mã {existingByIdCard.Id}).");
            }
        }

        // 2. Check duplicate walk-in profile by (FullName + PhoneNumber + DateOfBirth)
        var existingByDemographics = await patientRepository.FindDuplicateGuestAsync(
            fullName, phone, request.DateOfBirth, ct);
        if (existingByDemographics != null)
        {
            throw new PatientDuplicateException(
                "PATIENT_DUPLICATE_PROFILE",
                $"Hồ sơ bệnh nhân với số điện thoại {PiiMasker.Mask(phone)} và ngày sinh này đã tồn tại (mã {existingByDemographics.Id}).");
        }

        // 3. Patient code PAT-YYMMDD-XXXX from the atomic daily counter (BA:L32). WHY: the old code embedded
        // the CCCD in the id (PII in every URL/log) and used random suffixes that could collide.
        var localDate = ClinicTime.LocalDate(now);
        var seq = await sequences.NextAsync($"patient:{localDate:yyyy-MM-dd}", ct);
        if (seq > 9999) throw new SequenceExhaustedException("PAT-");
        var patientId = PatientIdentityRules.FormatPatientCode(localDate, seq);

        var dobUtc = request.DateOfBirth.HasValue
            ? (request.DateOfBirth.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.DateOfBirth.Value.Date, DateTimeKind.Utc)
                : request.DateOfBirth.Value.ToUniversalTime().Date)
            : (DateTime?)null;

        var entity = new Patient
        {
            Id = patientId,
            PatientCodeField = patientId,
            AccountId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(), // Unique reference so non-sparse AccountId index in MongoDB never collides
            IsGuest = true,
            FullName = fullName,
            DateOfBirth = dobUtc,
            GenderString = NormalizeGender(request.Gender),
            PhoneNumber = phone,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(),
            // WHY null (not the patient id) when absent: a fake CCCD would occupy the unique CCCD index.
            IdentityCardNumber = identityNumber,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            BloodType = string.IsNullOrWhiteSpace(request.BloodType) ? "Unknown" : request.BloodType.Trim().ToUpperInvariant(),
            EmergencyContactName = string.IsNullOrWhiteSpace(request.EmergencyContactName) ? null : request.EmergencyContactName.Trim(),
            EmergencyContactPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone) ? null : request.EmergencyContactPhone.Trim(),
            MedicalHistory = string.IsNullOrWhiteSpace(request.MedicalHistory) ? "Không có tiền sử bệnh lý đặc biệt" : request.MedicalHistory.Trim(),
            Allergies = request.Allergies?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList() ?? [],
            Vitals = request.Vitals == null
                ? null
                : new PatientVitals
                {
                    HeightCm = request.Vitals.HeightCm,
                    WeightKg = request.Vitals.WeightKg,
                    BloodPressure = string.IsNullOrWhiteSpace(request.Vitals.BloodPressure) ? null : request.Vitals.BloodPressure.Trim(),
                    HeartRateBpm = request.Vitals.HeartRateBpm,
                    TemperatureC = request.Vitals.TemperatureC,
                    Spo2Percent = request.Vitals.Spo2Percent
                },
            Dependents = [],
            CreatedBy = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await patientRepository.InsertAsync(entity, ct);

        logger.LogInformation(
            "Guest patient profile created: {PatientId} ({FullName}, Phone={Phone}) by {CreatedBy}",
            entity.Id, PiiMasker.MaskName(entity.FullName), PiiMasker.Mask(entity.PhoneNumber), createdByUserId ?? "ANONYMOUS");

        return MapToDto(entity, [], [], now);
    }

    /// <summary>
    /// UC30 - View Patient Information (Issue I2-031 | BR-092 / BR-IAM-02).
    /// Retrieves full clinical patient demographic profile, vital signs history, and access control audit.
    /// Non-staff callers are strictly restricted to their own patient records.
    /// </summary>
    public async Task<PatientInformationDto> GetPatientInformationAsync(
        string idOrCode,
        string? requesterUserId,
        bool isStaffOrAdmin,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new PatientNotFoundException(idOrCode);

        var now = clock.GetUtcNow().UtcDateTime;
        var patient = await patientRepository.GetByIdOrCodeAsync(idOrCode, ct)
                      ?? await patientRepository.GetByAccountIdAsync(idOrCode, ct);

        if (patient is null)
            throw new PatientNotFoundException(idOrCode);

        // Enforce HIPAA / clinical RBAC access control:
        // Non-staff (Patient role) can only view their own profile.
        if (!isStaffOrAdmin)
        {
            var isOwner = !string.IsNullOrWhiteSpace(requesterUserId) &&
                          (string.Equals(patient.AccountId, requesterUserId, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(patient.Id, requesterUserId, StringComparison.OrdinalIgnoreCase));

            if (!isOwner)
            {
                throw new ForbiddenAccessException(
                    "Bạn chỉ có quyền xem hồ sơ bệnh nhân của chính mình.");
            }
        }

        var appointments = await patientRepository.GetRecentAppointmentsAsync(patient.Id, 10, ct);
        var medicalRecords = await patientRepository.GetRecentMedicalRecordsAsync(patient.Id, 10, ct);

        return MapToDto(patient, appointments, medicalRecords, now);
    }

    public async Task<PatientInformationDto> UpdateMyPatientInformationAsync(
        string userId,
        UpdatePatientProfileRequest request,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var patient = await patientRepository.GetByAccountIdAsync(userId, ct)
                      ?? await patientRepository.GetByIdOrCodeAsync(userId, ct);

        var isNewPatient = false;
        if (patient is null)
        {
            isNewPatient = true;
            var account = await userRepository.FindByIdAsync(userId, ct);
            var patCode = account != null && !string.IsNullOrWhiteSpace(account.PatientCode)
                ? account.PatientCode
                : $"PAT-{userId[..Math.Min(8, userId.Length)].ToUpperInvariant()}";

            patient = new Patient
            {
                Id = patCode,
                PatientCodeField = patCode,
                AccountId = userId,
                IsGuest = false,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        // Validate CCCD duplicate if modified
        var cleanCccd = request.IdentityNumber.Trim();
        if (!string.Equals(patient.IdentityCardNumber, cleanCccd, StringComparison.OrdinalIgnoreCase))
        {
            var existingByCccd = await patientRepository.FindByIdentityNumberAsync(cleanCccd, ct);
            if (existingByCccd != null && !string.Equals(existingByCccd.Id, patient.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new PatientDuplicateException(
                    "MSG-ERR-REC-01",
                    $"Số CCCD {PiiMasker.Mask(cleanCccd)} đã được liên kết với một hồ sơ bệnh nhân khác.");
            }
        }

        patient.FullName = request.FullName.Trim();
        patient.PhoneNumber = request.PhoneNumber.Trim();
        patient.IdentityCardNumber = cleanCccd;
        patient.GenderString = NormalizeGender(request.Gender);
        if (request.DateOfBirth.HasValue)
        {
            patient.DateOfBirth = request.DateOfBirth.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.DateOfBirth.Value.Date, DateTimeKind.Utc)
                : request.DateOfBirth.Value.ToUniversalTime().Date;
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
            patient.Email = request.Email.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(request.Address))
            patient.Address = request.Address.Trim();

        patient.BloodType = string.IsNullOrWhiteSpace(request.BloodType) ? "Unknown" : request.BloodType.Trim().ToUpperInvariant();
        patient.MedicalHistory = string.IsNullOrWhiteSpace(request.MedicalHistory) ? "Chưa ghi nhận bệnh lý mãn tính" : request.MedicalHistory.Trim();
        patient.CoagulationDisorders = string.IsNullOrWhiteSpace(request.CoagulationDisorders) ? "Không có" : request.CoagulationDisorders.Trim();
        patient.CurrentMedications = string.IsNullOrWhiteSpace(request.CurrentMedications) ? "Không có" : request.CurrentMedications.Trim();
        patient.EmergencyContactName = request.EmergencyContactName.Trim();
        patient.EmergencyContactPhone = request.EmergencyContactPhone.Trim();
        patient.EmergencyContactRelationship = string.IsNullOrWhiteSpace(request.EmergencyContactRelationship) ? "Người thân" : request.EmergencyContactRelationship.Trim();

        if (request.Allergies != null)
        {
            patient.Allergies = request.Allergies.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
        }

        if (request.Vitals != null)
        {
            patient.Vitals = new PatientVitals
            {
                HeightCm = request.Vitals.HeightCm,
                WeightKg = request.Vitals.WeightKg,
                BloodPressure = request.Vitals.BloodPressure,
                HeartRateBpm = request.Vitals.HeartRateBpm,
                TemperatureC = request.Vitals.TemperatureC,
                Spo2Percent = request.Vitals.Spo2Percent
            };
        }

        patient.UpdatedAt = now;

        if (isNewPatient)
        {
            await patientRepository.InsertAsync(patient, ct);
        }
        else
        {
            await patientRepository.UpdateAsync(patient, ct);
        }

        // Synchronize basic fields to accounts collection if linked
        try
        {
            var account = await userRepository.FindByIdAsync(userId, ct);
            if (account != null)
            {
                account.FullName = patient.FullName;
                account.PhoneNumber = patient.PhoneNumber;
                account.DateOfBirth = patient.DateOfBirth;
                account.Gender = patient.GenderString;
                account.IdentityCardNumber = patient.IdentityCardNumber;
                account.Address = patient.Address;
                account.BloodType = patient.BloodType;
                account.MedicalProfileCompleted = true;
                account.UpdatedAt = now;
                await userRepository.UpdateAsync(account, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync patient update to user account {UserId}", userId);
        }

        var appointments = await patientRepository.GetRecentAppointmentsAsync(patient.Id, 10, ct);
        var medicalRecords = await patientRepository.GetRecentMedicalRecordsAsync(patient.Id, 10, ct);

        logger.LogInformation("Patient medical profile updated successfully for {PatientId} ({FullName})", patient.Id, PiiMasker.MaskName(patient.FullName));
        return MapToDto(patient, appointments, medicalRecords, now);
    }

    public async Task<PatientInformationDto> GetMyPatientInformationAsync(
        string userId,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var patient = await patientRepository.GetByAccountIdAsync(userId, ct)
                      ?? await patientRepository.GetByIdOrCodeAsync(userId, ct);

        if (patient is null)
        {
            var account = await userRepository.FindByIdAsync(userId, ct);
            patient = new Patient
            {
                Id = !string.IsNullOrWhiteSpace(account?.PatientCode) ? account.PatientCode : $"PAT-{userId[..Math.Min(8, userId.Length)].ToUpperInvariant()}",
                AccountId = userId,
                IsGuest = false,
                FullName = account?.FullName ?? "Bệnh Nhân",
                DateOfBirth = account?.DateOfBirth,
                GenderString = account?.Gender ?? "Unspecified",
                PhoneNumber = account?.PhoneNumber,
                Email = account?.Email,
                IdentityCardNumber = account?.IdentityCardNumber,
                Address = account?.Address,
                BloodType = account?.BloodType ?? "Unknown",
                CreatedAt = account?.CreatedAt ?? now,
                UpdatedAt = account?.UpdatedAt ?? now
            };
        }

        var appointments = await patientRepository.GetRecentAppointmentsAsync(patient.Id, 10, ct);
        var medicalRecords = await patientRepository.GetRecentMedicalRecordsAsync(patient.Id, 10, ct);

        return MapToDto(patient, appointments, medicalRecords, now);
    }

    public async Task<PagedResult<PatientInformationDto>> SearchPatientsAsync(
        PatientSearchRequest request,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var paged = await patientRepository.SearchAsync(request, ct);
        var dtos = paged.Items.Select(p => MapToDto(p, [], [], now)).ToList();
        return new PagedResult<PatientInformationDto>(dtos, paged.Page, paged.PageSize, paged.TotalItems);
    }

    private static string NormalizeGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return "Unspecified";
        return gender.Trim().ToLowerInvariant() switch
        {
            "male" or "nam" => "Male",
            "female" or "nu" or "nữ" => "Female",
            "other" or "khac" or "khác" => "Other",
            _ => "Unspecified"
        };
    }

    private static PatientInformationDto MapToDto(
        Patient p,
        IReadOnlyList<PatientAppointmentSummaryDto> appointments,
        IReadOnlyList<PatientMedicalRecordSummaryDto> medicalRecords,
        DateTime now)
    {
        int? age = null;
        if (p.DateOfBirth is { } dob)
        {
            var years = now.Year - dob.Year;
            if (dob.Date > now.Date.AddYears(-years)) years--;
            age = Math.Max(0, years);
        }

        return new PatientInformationDto
        {
            Id = p.Id,
            PatientCode = p.PatientCode,
            AccountId = p.AccountId,
            IsGuest = p.IsGuest || string.IsNullOrWhiteSpace(p.AccountId),
            FullName = p.FullName,
            DateOfBirth = p.DateOfBirth,
            Age = age,
            Gender = p.GenderString,
            PhoneNumber = p.PhoneNumber,
            Email = p.Email,
            IdentityNumber = p.IdentityCardNumber,
            Address = p.Address,
            BloodType = p.BloodType,
            EmergencyContactName = p.EmergencyContactName,
            EmergencyContactPhone = p.EmergencyContactPhone,
            EmergencyContactRelationship = p.EmergencyContactRelationship,
            CurrentMedications = p.CurrentMedications,
            CoagulationDisorders = p.CoagulationDisorders,
            IsProfileComplete = !string.IsNullOrWhiteSpace(p.FullName)
                && p.DateOfBirth.HasValue
                && !string.IsNullOrWhiteSpace(p.PhoneNumber)
                && !string.IsNullOrWhiteSpace(p.IdentityCardNumber)
                && !string.IsNullOrWhiteSpace(p.BloodType) && !string.Equals(p.BloodType, "Unknown", StringComparison.OrdinalIgnoreCase)
                && p.Vitals != null && (p.Vitals.HeightCm ?? 0) > 0 && (p.Vitals.WeightKg ?? 0) > 0
                && !string.IsNullOrWhiteSpace(p.EmergencyContactName)
                && !string.IsNullOrWhiteSpace(p.EmergencyContactPhone),
            MedicalHistory = p.MedicalHistory,
            Allergies = p.Allergies ?? [],
            Vitals = p.Vitals == null
                ? null
                : new PatientVitalsDto
                {
                    HeightCm = p.Vitals.HeightCm,
                    WeightKg = p.Vitals.WeightKg,
                    BloodPressure = p.Vitals.BloodPressure,
                    HeartRateBpm = p.Vitals.HeartRateBpm,
                    TemperatureC = p.Vitals.TemperatureC,
                    Spo2Percent = p.Vitals.Spo2Percent
                },
            Dependents = (p.Dependents ?? []).Select(d => new PatientDependentDto
            {
                DependentId = d.DependentId,
                FullName = d.FullName,
                DateOfBirth = d.DateOfBirth,
                Gender = d.Gender,
                Relationship = d.Relationship,
                IdentityNumber = d.IdentityNumber
            }).ToList(),
            CreatedBy = p.CreatedBy,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            RecentAppointments = appointments,
            RecentMedicalRecords = medicalRecords
        };
    }
}
