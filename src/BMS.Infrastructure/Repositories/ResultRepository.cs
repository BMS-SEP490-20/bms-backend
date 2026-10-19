using BMS.Core.Entities;
using MongoDB.Driver;
using BMS.Core.Interfaces;

namespace BMS.Infrastructure.Repositories;

public class ResultRepository : IResultRepository
{
    private readonly IMongoCollection<TestResult> _results;
    private readonly IMongoCollection<Sample> _samples;

    public ResultRepository(IMongoDatabase database)
    {
        _results = database.GetCollection<TestResult>("test_results");
        _samples = database.GetCollection<Sample>("blood_samples");
    }

    public async Task<Sample?> GetSampleAsync(string sampleId, CancellationToken ct = default)
        => await _samples.Find(x => x.Id == sampleId || x.Barcode == sampleId).FirstOrDefaultAsync(ct);

    public async Task InsertAsync(TestResult result, CancellationToken ct = default)
        => await _results.InsertOneAsync(result, cancellationToken: ct);

    public async Task<string> GenerateNextResultCodeAsync(DateTime date, CancellationToken ct = default)
    {
        var prefix = $"RES-{date:yyyyMMdd}-";
        var last = await _results
            .Find(x => x.ResultCode.StartsWith(prefix))
            .SortByDescending(x => x.ResultCode)
            .Limit(1)
            .FirstOrDefaultAsync(ct);
        var seq = 1;
        if (last != null)
        {
            var part = last.ResultCode.Split('-').LastOrDefault();
            if (int.TryParse(part, out var n)) seq = n + 1;
        }
        return $"{prefix}{seq:D3}";
    }

    public async Task UpdateSampleStatusAsync(string sampleId, string status, CancellationToken ct = default)
    {
        var update = Builders<Sample>.Update.Set(x => x.StatusString, status);
        await _samples.UpdateOneAsync(x => x.Id == sampleId, update, cancellationToken: ct);
    }
}
