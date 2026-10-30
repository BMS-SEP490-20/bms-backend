namespace BMS.Application.Interfaces;

/// <summary>
/// Answers "does this logged-in account own this patient record?" for Patient-scoped endpoints (TIEU-CHI R14, anti-BOLA).
/// </summary>
public interface IPatientOwnership
{
    /// <summary>
    /// True when <paramref name="patientId"/> (patient <c>_id</c>, patient code, or the account ID itself) belongs to
    /// <paramref name="accountId"/>. The account ID must come from the token, never from the request.
    /// </summary>
    Task<bool> IsOwnPatientAsync(string accountId, string patientId, CancellationToken ct = default);
}
