using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>Specimens created at collection time (UC37/UC39) in <c>blood_samples</c>.</summary>
public interface ISpecimenRepository
{
    /// <summary>Inserts the tubes of one collection (unique SMP- barcode index rejects duplicates).</summary>
    Task InsertManyAsync(IReadOnlyCollection<Sample> samples, CancellationToken ct = default);

    /// <summary>Tubes of an order, by draw order.</summary>
    Task<IReadOnlyList<Sample>> GetByOrderAsync(string orderId, CancellationToken ct = default);
}
