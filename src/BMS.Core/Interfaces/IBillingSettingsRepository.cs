using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>BHYT group-code catalog and Manager fee parameters (collections <c>bhyt_group_codes</c>, <c>billing_settings</c>).</summary>
public interface IBillingSettingsRepository
{
    /// <summary>Codes currently accepted on a BHYT card.</summary>
    Task<IReadOnlyList<string>> GetActiveBhytGroupCodesAsync(CancellationToken ct = default);

    /// <summary>All catalog rows (active and inactive), by code.</summary>
    Task<IReadOnlyList<BhytGroupCode>> GetBhytGroupCodesAsync(CancellationToken ct = default);

    /// <summary>The row for <paramref name="code"/>, or null.</summary>
    Task<BhytGroupCode?> GetBhytGroupCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Creates or updates one code (atomic upsert on the code id) and returns the stored row.</summary>
    Task<BhytGroupCode> UpsertBhytGroupCodeAsync(string code, string? description, bool isActive, string actorUserId, DateTime nowUtc,
        CancellationToken ct = default);

    /// <summary>Inserts the codes that do not exist yet ($setOnInsert); existing rows, including deactivated ones, are untouched.</summary>
    /// <returns>Number of rows inserted.</returns>
    Task<int> SeedBhytGroupCodesAsync(IEnumerable<string> codes, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>The fee-policy document, or null when the Manager never set it (defaults apply).</summary>
    Task<FeePolicySettings?> GetFeePolicyAsync(CancellationToken ct = default);

    /// <summary>Stores the priority-discount parameters (upsert of the single document).</summary>
    Task<FeePolicySettings> SaveFeePolicyAsync(decimal percent, decimal? maxAmount, string actorUserId, DateTime nowUtc,
        CancellationToken ct = default);
}
