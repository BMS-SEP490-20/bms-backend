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
    TimeProvider clock,
    ILogger<PatientService> logger) : IPatientService
{
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

        // 1. Check duplicate identity card number (CCCD / CMND)
        if (!string.IsNullOrWhiteSpace(identityNumber))
        {
            var existingByIdCard = await patientRepository.FindByIdentityNumberAsync(identityNumber, ct);
            if (existingByIdCard != null)
            {
                throw new PatientDuplicateException(
                    "PATIENT_IDENTITY_EXISTS",
                    $"Số CCCD/CMND '{identityNumber}' đã được đăng ký cho hồ sơ bệnh nhân '{existingByIdCard.FullName}' (Mã: {existingByIdCard.Id}).");
            }
        }

        // 2. Check duplicate walk-in profile by (FullName + PhoneNumber + DateOfBirth)
        var existingByDemographics = await patientRepository.FindDuplicateGuestAsync(
            fullName, phone, request.DateOfBirth, ct);
        if (existingByDemographics != null)
        {
            throw new PatientDuplicateException(
                "PATIENT_DUPLICATE_PROFILE",
                $"Hồ sơ bệnh nhân '{fullName}' với số điện thoại '{phone}' đã tồn tại trong hệ thống (Mã: {existingByDemographics.Id}).");
        }

        // 3. Generate deterministic or unique patient ID following BMS_Database convention (PAT-xxx)
        var patientId = !string.IsNullOrWhiteSpace(identityNumber)
            ? $"PAT-{identityNumber}"
            : $"PAT-GUEST-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(1000, 10000)}";

        if (await patientRepository.GetByIdOrCodeAsync(patientId, ct) != null)
        {
            patientId = $"PAT-GUEST-{now:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100, 1000)}";
        }

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
            IdentityCardNumber = identityNumber ?? patientId,
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
            entity.Id, entity.FullName, entity.PhoneNumber, createdByUserId ?? "ANONYMOUS");

        return MapToDto(entity, [], [], now);
    }

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

    public async Task<PatientInformationDto> GetMyPatientInformationAsync(
        string userId,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var patient = await patientRepository.GetByAccountIdAsync(userId, ct)
                      ?? await patientRepository.GetByIdOrCodeAsync(userId, ct);

        if (patient is null)
        {
            // Fallback: check if account exists in accounts collection and construct profile view
            var account = await userRepository.FindByIdAsync(userId, ct);
            if (account is null)
                throw new PatientNotFoundException(userId);

            patient = new Patient
            {
                Id = !string.IsNullOrWhiteSpace(account.PatientCode) ? account.PatientCode : $"PAT-{account.Id}",
                AccountId = account.Id,
                IsGuest = false,
                FullName = account.FullName,
                DateOfBirth = account.DateOfBirth,
                GenderString = account.Gender ?? "Unspecified",
                PhoneNumber = account.PhoneNumber,
                Email = account.Email,
                IdentityCardNumber = account.IdentityCardNumber,
                Address = account.Address,
                BloodType = account.BloodType ?? "Unknown",
                CreatedAt = account.CreatedAt,
                UpdatedAt = account.UpdatedAt
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
