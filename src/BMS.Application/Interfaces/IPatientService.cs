using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Patients;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

public interface IPatientRepository
{
    Task<Patient?> GetByIdOrCodeAsync(string idOrCode, CancellationToken ct = default);
    Task<Patient?> GetByAccountIdAsync(string accountId, CancellationToken ct = default);
    Task<Patient?> FindByIdentityNumberAsync(string identityNumber, CancellationToken ct = default);
    Task<Patient?> FindDuplicateGuestAsync(string fullName, string phoneNumber, DateTime? dateOfBirth, CancellationToken ct = default);
    Task<PagedResult<Patient>> SearchAsync(PatientSearchRequest request, CancellationToken ct = default);
    Task InsertAsync(Patient patient, CancellationToken ct = default);
    Task<IReadOnlyList<PatientAppointmentSummaryDto>> GetRecentAppointmentsAsync(string patientId, int limit = 10, CancellationToken ct = default);
    Task<IReadOnlyList<PatientMedicalRecordSummaryDto>> GetRecentMedicalRecordsAsync(string patientId, int limit = 10, CancellationToken ct = default);
}

public interface IPatientService
{
    /// <summary>
    /// Creates a new guest (walk-in) patient profile with duplicate identity/demographic checking.
    /// </summary>
    Task<PatientInformationDto> CreateGuestProfileAsync(
        CreateGuestProfileRequest request,
        string? createdByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves full patient demographic, identity, clinical vitals, and medical history with RBAC ownership verification.
    /// </summary>
    Task<PatientInformationDto> GetPatientInformationAsync(
        string idOrCode,
        string? requesterUserId,
        bool isStaffOrAdmin,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the authenticated patient's own profile.
    /// </summary>
    Task<PatientInformationDto> GetMyPatientInformationAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Searches and filters patient profiles for clinical staff/receptionists.
    /// </summary>
    Task<PagedResult<PatientInformationDto>> SearchPatientsAsync(
        PatientSearchRequest request,
        CancellationToken ct = default);
}
