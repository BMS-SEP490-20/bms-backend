using BMS.Application.Services.Billing;

namespace BMS.Application.Interfaces;

/// <summary>Loads the current <see cref="FeePolicy"/> (BHYT group catalog + Manager fee parameters) from the database.</summary>
public interface IFeePolicyProvider
{
    /// <summary>Current policy; no caching so a catalog change applies to the next calculation.</summary>
    Task<FeePolicy> GetAsync(CancellationToken ct = default);
}
