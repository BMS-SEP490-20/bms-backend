using BMS.WalkIn.DTOs;

namespace BMS.WalkIn.Services;

public interface IWalkInService
{
    Task<RegisterWalkInResponse> RegisterAsync(
        RegisterWalkInRequest request,
        string? receptionistUserId,
        CancellationToken ct = default);

    Task<PatientLookupResponse> LookupByIdentityAsync(
        string identityNumber,
        CancellationToken ct = default);

    Task<PatientLookupResponse> LookupByPhoneAsync(
        string phone,
        CancellationToken ct = default);
}
