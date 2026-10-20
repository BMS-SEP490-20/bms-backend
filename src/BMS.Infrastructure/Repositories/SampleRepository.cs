using BMS.Core.Entities;
using MongoDB.Driver;
using BMS.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BMS.Infrastructure.Repositories;

public class SampleRepository : ISampleRepository
{
    private readonly IMongoCollection<Sample> _collection;

    public SampleRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<Sample>("blood_samples");
    }

    public async Task<Sample?> FindByCodeAsync(string code, CancellationToken ct = default)
        => await _collection.Find(x => x.Id == code || x.Barcode == code).FirstOrDefaultAsync(ct);

    public async Task CreateAsync(Sample sample, CancellationToken ct = default)
        => await _collection.InsertOneAsync(sample, cancellationToken: ct);

    public async Task UpdateAsync(Sample sample, CancellationToken ct = default)
        => await _collection.ReplaceOneAsync(x => x.Id == sample.Id, sample, cancellationToken: ct);

    public async Task<List<Sample>> ListPendingCheckInAsync(CancellationToken ct = default)
    {
        // Collected / InTransit awaiting lab reception.
        return await _collection
            .Find(x => x.StatusString == "Collected"
                       || x.StatusString == "InTransit"
                       || x.StatusString == nameof(SampleStatus.Collected)
                       || x.StatusString == nameof(SampleStatus.InTransit))
            .SortByDescending(x => x.CollectedAt)
            .Limit(100)
            .ToListAsync(ct);
    }
}
