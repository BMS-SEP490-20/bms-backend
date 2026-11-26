using BMS.Application.DTOs.Patients;
using BMS.Application.Interfaces;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using BMS.WalkIn.DTOs;

namespace BMS.Application.Services;

/// <summary>
/// UC26 – Register walk-in patient.
/// Delegates to existing PatientService.CreateGuestProfileAsync and IPatientRepository.
/// </summary>
public sealed class WalkInService(
    IPatientService patientService,
    IPatientRepository patientRepository,
    IUserRepository? userRepository = null) : IWalkInService
{
    public async Task<RegisterWalkInResponse> RegisterAsync(
        RegisterWalkInRequest request,
        string? receptionistUserId,
        CancellationToken ct = default)
    {
        PatientIdentityRules.Validate(request.IdentityNumber, request.Phone, request.DateOfBirth);

        var cleanCccd = request.IdentityNumber.Trim();
        var cleanPhone = request.Phone.Trim();
        var cleanEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        // Enforce 1-to-1 identity binding across CCCD, Phone, and Email in DB
        var byCccd = await LookupByIdentityAsync(cleanCccd, ct);
        if (byCccd.Exists)
        {
            throw new PatientDuplicateException(
                "MSG-ERR-REC-01",
                $"Số CCCD {MaskCccd(cleanCccd)} đã tồn tại — thuộc bệnh nhân {byCccd.FullName} ({byCccd.PatientCode ?? byCccd.PatientId}, SĐT: {byCccd.Phone ?? "—"}, Email: {byCccd.Email ?? "—"}).");
        }

        var byPhone = await LookupByPhoneAsync(cleanPhone, ct);
        if (byPhone.Exists && !string.Equals(byPhone.IdentityNumber, cleanCccd, StringComparison.OrdinalIgnoreCase))
        {
            throw new PatientDuplicateException(
                "PATIENT_PHONE_BOUND",
                $"Số điện thoại {cleanPhone} đã gắn với bệnh nhân {byPhone.FullName} ({byPhone.PatientCode ?? byPhone.PatientId}, CCCD: {MaskCccd(byPhone.IdentityNumber)}, Email: {byPhone.Email ?? "—"}).");
        }

        if (!string.IsNullOrWhiteSpace(cleanEmail))
        {
            var byEmail = await LookupByEmailAsync(cleanEmail, ct);
            if (byEmail.Exists && !string.Equals(byEmail.IdentityNumber, cleanCccd, StringComparison.OrdinalIgnoreCase))
            {
                throw new PatientDuplicateException(
                    "PATIENT_EMAIL_BOUND",
                    $"Email {cleanEmail} đã gắn với bệnh nhân {byEmail.FullName} ({byEmail.PatientCode ?? byEmail.PatientId}, CCCD: {MaskCccd(byEmail.IdentityNumber)}, SĐT: {byEmail.Phone ?? "—"}).");
            }
        }

        var ageYears = CalculateAgeYears(request.DateOfBirth);
        var priority = string.IsNullOrWhiteSpace(request.Priority)
            ? "NORMAL"
            : request.Priority.Trim().ToUpperInvariant();
        if (ageYears >= 80 || ageYears < 1)
            priority = "HIGH";

        var createRequest = new CreateGuestProfileRequest
        {
            FullName = request.FullName,
            PhoneNumber = request.Phone,
            DateOfBirth = request.DateOfBirth.Date,
            Gender = request.Gender,
            IdentityNumber = request.IdentityNumber,
            Email = request.Email,
            Address = request.Address,
            BloodType = request.BloodType,
            MedicalHistory = request.MedicalHistory,
            Allergies = request.Allergies,
            EmergencyContactName = request.EmergencyContactName,
            EmergencyContactPhone = request.EmergencyContactPhone,
            Vitals = request.Vitals == null
                ? null
                : new PatientVitalsDto
                {
                    HeightCm = request.Vitals.HeightCm,
                    WeightKg = request.Vitals.WeightKg,
                    BloodPressure = request.Vitals.BloodPressure,
                    HeartRateBpm = request.Vitals.HeartRateBpm,
                    TemperatureC = request.Vitals.TemperatureC,
                    Spo2Percent = request.Vitals.Spo2Percent
                }
        };

        // WHY no try/catch: PatientDuplicateException (409, MSG-ERR-REC-01) and validation errors reach the
        // global handler with their stable codes instead of being re-wrapped by message prefix.
        var dto = await patientService.CreateGuestProfileAsync(
            createRequest,
            receptionistUserId,
            ct);

        return new RegisterWalkInResponse
        {
            PatientId = dto.Id,
            PatientCode = dto.PatientCode ?? dto.Id,
            FullName = dto.FullName,
            IdentityNumber = dto.IdentityNumber ?? request.IdentityNumber,
            Phone = dto.PhoneNumber ?? request.Phone,
            Gender = dto.Gender,
            DateOfBirth = dto.DateOfBirth,
            Email = dto.Email ?? request.Email,
            PortalUsername = dto.PortalUsername,
            TemporaryPassword = dto.TemporaryPassword,
            AccountAutoCreated = dto.AccountAutoCreated,
            IsGuest = dto.IsGuest,
            Priority = priority,
            Message = "Walk-in guest profile created successfully."
        };
    }

    public async Task<PatientLookupResponse> LookupByIdentityAsync(
        string identityNumber,
        CancellationToken ct = default)
    {
        var trimmed = (identityNumber ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return new PatientLookupResponse { Exists = false };

        var patient = await patientRepository.FindByIdentityNumberAsync(trimmed, ct);
        return await MapLookupAsync(patient, ct);
    }

    public async Task<PatientLookupResponse> LookupByPhoneAsync(
        string phone,
        CancellationToken ct = default)
    {
        var trimmed = (phone ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return new PatientLookupResponse { Exists = false };

        var paged = await patientRepository.SearchAsync(
            new PatientSearchRequest
            {
                Keyword = trimmed,
                Page = 1,
                PageSize = 10
            },
            ct);

        var patient = paged.Items.FirstOrDefault(p =>
            string.Equals(p.PhoneNumber?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));

        return await MapLookupAsync(patient, ct);
    }

    public async Task<PatientLookupResponse> LookupByEmailAsync(
        string email,
        CancellationToken ct = default)
    {
        var trimmed = (email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return new PatientLookupResponse { Exists = false };

        var paged = await patientRepository.SearchAsync(
            new PatientSearchRequest
            {
                Keyword = trimmed,
                Page = 1,
                PageSize = 10
            },
            ct);

        var patient = paged.Items.FirstOrDefault(p =>
            string.Equals(p.Email?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        if (patient != null)
            return await MapLookupAsync(patient, ct);

        if (userRepository != null)
        {
            var account = await userRepository.FindByEmailAsync(trimmed, ct);
            if (account != null)
            {
                var linkedPatient = await patientRepository.GetByAccountIdAsync(account.Id, ct);
                if (linkedPatient != null)
                    return await MapLookupAsync(linkedPatient, ct);

                if (!string.IsNullOrWhiteSpace(account.IdentityCardNumber))
                {
                    var byId = await patientRepository.FindByIdentityNumberAsync(account.IdentityCardNumber, ct);
                    if (byId != null)
                        return await MapLookupAsync(byId, ct);
                }

                return new PatientLookupResponse
                {
                    Exists = true,
                    PatientId = account.PatientCode ?? account.Id,
                    PatientCode = account.PatientCode ?? account.Id,
                    FullName = account.FullName,
                    IdentityNumber = account.IdentityCardNumber,
                    Phone = account.PhoneNumber,
                    Email = account.Email,
                    Address = account.Address,
                    BloodType = account.BloodType,
                    DateOfBirth = account.DateOfBirth,
                    Gender = account.Gender
                };
            }
        }

        return new PatientLookupResponse { Exists = false };
    }

    private async Task<PatientLookupResponse> MapLookupAsync(BMS.Core.Entities.Patient? p, CancellationToken ct)
    {
        if (p is null)
            return new PatientLookupResponse { Exists = false };

        string? resolvedEmail = p.Email;
        string? resolvedPhone = p.PhoneNumber;
        if (userRepository != null && !string.IsNullOrWhiteSpace(p.AccountId) &&
            (string.IsNullOrWhiteSpace(resolvedEmail) || string.IsNullOrWhiteSpace(resolvedPhone)))
        {
            try
            {
                var acc = await userRepository.GetByIdAsync(p.AccountId, ct);
                if (acc != null)
                {
                    if (string.IsNullOrWhiteSpace(resolvedEmail)) resolvedEmail = acc.Email;
                    if (string.IsNullOrWhiteSpace(resolvedPhone)) resolvedPhone = acc.PhoneNumber;
                }
            }
            catch { /* non-fatal */ }
        }

        return new PatientLookupResponse
        {
            Exists = true,
            PatientId = p.Id,
            PatientCode = p.PatientCode,
            FullName = p.FullName,
            IdentityNumber = p.IdentityCardNumber,
            Phone = resolvedPhone,
            Email = resolvedEmail,
            Address = p.Address,
            BloodType = p.BloodType,
            DateOfBirth = p.DateOfBirth,
            Gender = p.GenderString
        };
    }

    private static int CalculateAgeYears(DateTime dob)
    {
        var today = DateTime.UtcNow.Date;
        var age = today.Year - dob.Year;
        if (dob.Date > today.AddYears(-age)) age--;
        return age;
    }

    private static string MaskCccd(string? cccd)
    {
        if (string.IsNullOrWhiteSpace(cccd)) return "—";
        var s = cccd.Trim();
        return s.Length >= 4 ? $"********{s[^4..]}" : "****";
    }
}
