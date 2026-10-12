using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for OTP token persistence and query operations in MongoDB.
/// Preserves the Dependency Inversion Principle within Clean Architecture.
/// </summary>
public interface IOtpRepository
{
    /// <summary>
    /// Persists a newly generated OTP entity into the data store.
    /// </summary>
    Task CreateAsync(OtpTokenEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recent active and unexpired OTP entity for a recipient and purpose.
    /// </summary>
    Task<OtpTokenEntity?> GetLatestActiveByRecipientAsync(string recipient, string purpose, CancellationToken ct = default);

    /// <summary>
    /// Updates mutable fields of an OTP entity (e.g., attempt counts, used status).
    /// </summary>
    Task UpdateAsync(OtpTokenEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Invalidates all existing unconsumed OTP tokens for the specified recipient and purpose.
    /// </summary>
    Task InvalidatePreviousOtpsAsync(string recipient, string purpose, CancellationToken ct = default);

    /// <summary>
    /// Counts the number of OTP requests made within a given sliding time window for Rate Limiting.
    /// </summary>
    Task<int> CountRecentRequestsAsync(string recipient, TimeSpan window, CancellationToken ct = default);
}
