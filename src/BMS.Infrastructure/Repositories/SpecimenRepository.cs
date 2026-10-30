using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <inheritdoc />
public sealed class SpecimenRepository(IMongoDatabase database) : ISpecimenRepository
{
    /// <summary>Collection shared with the existing sample screens.</summary>
    public const string CollectionName = "blood_samples";

    private readonly IMongoCollection<Sample> _samples = database.GetCollection<Sample>(CollectionName);

    /// <summary>Unique SMP- barcode (partial on smp_day) and lookup by order.</summary>
    public static IEnumerable<CreateIndexModel<Sample>> Indexes()
    {
        var k = Builders<Sample>.IndexKeys;
        yield return new(k.Ascending(s => s.Barcode), new CreateIndexOptions<Sample>
        {
            Name = "ux_blood_samples_smp_barcode",
            Unique = true,
            PartialFilterExpression = Builders<Sample>.Filter.Exists(s => s.SmpDay)
        });
        yield return new(k.Ascending(s => s.TestOrderId), new CreateIndexOptions { Name = "ix_blood_samples_order" });
    }

    /// <inheritdoc />
    public Task InsertManyAsync(IReadOnlyCollection<Sample> samples, CancellationToken ct = default) =>
        _samples.InsertManyAsync(samples, new InsertManyOptions { IsOrdered = true }, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Sample>> GetByOrderAsync(string orderId, CancellationToken ct = default) =>
        await _samples.Find(s => s.TestOrderId == orderId).SortBy(s => s.DrawOrder).ToListAsync(ct);
}
