using BMS.Application.DTOs.Patients;
using BMS.Application.Interfaces;
using BMS.Core.Exceptions;
using BMS.WalkIn.DTOs;

namespace BMS.WalkIn.Services;

/// <summary>
/// UC26 – Register walk-in patient.
/// Delegates to existing PatientService.CreateGuestProfileAsync and IPatientRepository.
/// </summary>
public sealed class WalkInService(
    IPatientService patientService,
    IPatientRepository patientRepository) : IWalkInService
{
    public async Task<RegisterWalkInResponse> RegisterAsync(
        RegisterWalkInRequest request,
        string? receptionistUserId,
        CancellationToken ct = default)
    {
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

        try
        {
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
                IsGuest = dto.IsGuest,
                Priority = priority,
                Message = "Walk-in guest profile created successfully."
            };
        }
        catch (PatientDuplicateException ex)
        {
            throw new InvalidOperationException($"MSG-ERR-WALKIN-01: {ex.Message}", ex);
        }
    }

    public async Task<PatientLookupResponse> LookupByIdentityAsync(
        string identityNumber,
        CancellationToken ct = default)
    {
        var patient = await patientRepository.FindByIdentityNumberAsync(identityNumber.Trim(), ct);
        return MapLookup(patient);
    }

    public async Task<PatientLookupResponse> LookupByPhoneAsync(
        string phone,
        CancellationToken ct = default)
    {
        var paged = await patientRepository.SearchAsync(
            new PatientSearchRequest
            {
                Keyword = phone.Trim(),
                Page = 1,
                PageSize = 5
            },
            ct);

        var patient = paged.Items.FirstOrDefault(p =>
            string.Equals(p.PhoneNumber, phone.Trim(), StringComparison.OrdinalIgnoreCase));

        return MapLookup(patient);
    }

    private static PatientLookupResponse MapLookup(BMS.Core.Entities.Patient? p)
    {
        if (p is null)
            return new PatientLookupResponse { Exists = false };

        return new PatientLookupResponse
        {
            Exists = true,
            PatientId = p.Id,
            FullName = p.FullName,
            IdentityNumber = p.IdentityCardNumber,
            Phone = p.PhoneNumber,
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
}
