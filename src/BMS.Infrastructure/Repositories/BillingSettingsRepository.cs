using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <inheritdoc />
public sealed class BillingSettingsRepository(IMongoDatabase database) : IBillingSettingsRepository
{
    /// <summary>BHYT group-code catalog collection.</summary>
    public const string BhytGroupCodesCollection = "bhyt_group_codes";

    /// <summary>Billing parameters collection (one document per setting group).</summary>
    public const string SettingsCollection = "billing_settings";

    private readonly IMongoCollection<BhytGroupCode> _codes = database.GetCollection<BhytGroupCode>(BhytGroupCodesCollection);
    private readonly IMongoCollection<FeePolicySettings> _settings = database.GetCollection<FeePolicySettings>(SettingsCollection);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetActiveBhytGroupCodesAsync(CancellationToken ct = default) =>
        await _codes.Find(c => c.IsActive).Project(c => c.Code).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BhytGroupCode>> GetBhytGroupCodesAsync(CancellationToken ct = default) =>
        await _codes.Find(FilterDefinition<BhytGroupCode>.Empty).SortBy(c => c.Code).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<BhytGroupCode?> GetBhytGroupCodeAsync(string code, CancellationToken ct = default) =>
        await _codes.Find(c => c.Code == code).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<BhytGroupCode> UpsertBhytGroupCodeAsync(string code, string? description, bool isActive, string actorUserId,
        DateTime nowUtc, CancellationToken ct = default)
    {
        var update = Builders<BhytGroupCode>.Update
            .Set(c => c.IsActive, isActive)
            .Set(c => c.UpdatedAt, nowUtc)
            .Set(c => c.UpdatedBy, actorUserId)
            .SetOnInsert(c => c.Seeded, false);
        update = description != null
            ? update.Set(c => c.Description, description)
            : update.SetOnInsert(c => c.Description, string.Empty);
        return await _codes.FindOneAndUpdateAsync(c => c.Code == code, update,
            new FindOneAndUpdateOptions<BhytGroupCode> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, ct);
    }

    /// <inheritdoc />
    public async Task<int> SeedBhytGroupCodesAsync(IEnumerable<string> codes, DateTime nowUtc, CancellationToken ct = default)
    {
        var models = codes.Select(code => new UpdateOneModel<BhytGroupCode>(
            Builders<BhytGroupCode>.Filter.Eq(c => c.Code, code),
            Builders<BhytGroupCode>.Update
                .SetOnInsert(c => c.Description, string.Empty)
                .SetOnInsert(c => c.IsActive, true)
                .SetOnInsert(c => c.Seeded, true)
                .SetOnInsert(c => c.UpdatedAt, nowUtc)) { IsUpsert = true }).ToList();
        if (models.Count == 0) return 0;
        var result = await _codes.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false }, ct);
        return result.Upserts.Count;
    }

    /// <inheritdoc />
    public async Task<FeePolicySettings?> GetFeePolicyAsync(CancellationToken ct = default) =>
        await _settings.Find(s => s.Id == FeePolicySettings.DocumentId).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<FeePolicySettings> SaveFeePolicyAsync(decimal percent, decimal? maxAmount, string actorUserId, DateTime nowUtc,
        CancellationToken ct = default) =>
        await _settings.FindOneAndUpdateAsync(s => s.Id == FeePolicySettings.DocumentId,
            Builders<FeePolicySettings>.Update
                .Set(s => s.PriorityDiscountPercent, percent)
                .Set(s => s.PriorityDiscountMaxAmount, maxAmount)
                .Set(s => s.UpdatedAt, nowUtc)
                .Set(s => s.UpdatedBy, actorUserId),
            new FindOneAndUpdateOptions<FeePolicySettings> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, ct);
}
