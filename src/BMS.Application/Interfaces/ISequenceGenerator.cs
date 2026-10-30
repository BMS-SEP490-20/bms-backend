namespace BMS.Application.Interfaces;

/// <summary>
/// Atomic, persistent counters (MongoDB <c>counters</c> collection, <c>$inc</c> with upsert).
/// </summary>
/// <remarks>
/// WHY: queue numbers (Q-), patient codes (PAT-) and sample barcodes (SMP-) must stay unique under concurrent
/// requests and across app instances/restarts. Reading "max + 1" from the data (the old approach) hands the
/// same number to two parallel requests; an in-process lock does not help with more than one API instance.
/// Unique indexes on the generated codes are the second line of defence.
/// </remarks>
public interface ISequenceGenerator
{
    /// <summary>Atomically increments the counter <paramref name="key"/> and returns the new value (first call → 1).</summary>
    /// <param name="key">Counter id, e.g. <c>queue:2026-10-30</c>.</param>
    /// <param name="ct">Cancels the database call.</param>
    Task<long> NextAsync(string key, CancellationToken ct);
}
