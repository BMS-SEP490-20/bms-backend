using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB implementation of <see cref="IBloodSampleRepository"/> on <c>blood_samples</c>.
/// Replaces the legacy EF Core repository; reuses the existing <see cref="Sample"/> mapping
/// so Print Barcodes / Validate Sample operate on the same specimens as the lab queue.
/// </summary>
public sealed class BloodSampleRepository : IBloodSampleRepository
{
    private readonly IMongoCollection<Sample> _samples;

    /// <summary>Creates the repository bound to <c>blood_samples</c>.</summary>
    public BloodSampleRepository(IMongoDatabase database)
    {
        _samples = database.GetCollection<Sample>("blood_samples");
    }

    private static FilterDefinitionBuilder<Sample> F => Builders<Sample>.Filter;
    private static FilterDefinition<Sample> Active => F.Ne(x => x.IsDeleted, true);
    private static FilterDefinition<Sample> ByIdOrBarcode(string id) => F.Or(F.Eq(x => x.Id, id), F.Eq(x => x.Barcode, id)) & Active;

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Sample> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var total = await _samples.CountDocumentsAsync(Active, cancellationToken: ct);
        var items = await _samples.Find(Active).SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<Sample?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _samples.Find(ByIdOrBarcode(id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Sample>> GetByAppointmentIdAsync(string appointmentId, CancellationToken ct = default) =>
        await _samples.Find(F.Eq(x => x.TestOrderId, appointmentId) & Active).SortBy(x => x.CreatedAt).Limit(200).ToListAsync(ct);

    /// <inheritdoc />
    public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct = default) =>
        _samples.Find(F.Eq(x => x.Barcode, barcode)).AnyAsync(ct);

    /// <inheritdoc />
    public Task CreateAsync(Sample entity, CancellationToken ct = default) =>
        _samples.InsertOneAsync(entity, cancellationToken: ct);

    /// <inheritdoc />
    public async Task<bool> UpdateFieldsAsync(string id, SampleFieldChanges c, SampleStatusHistoryEntry? history, CancellationToken ct = default)
    {
        var u = Builders<Sample>.Update;
        var updates = new List<UpdateDefinition<Sample>> { u.Set(x => x.UpdatedAt, DateTime.UtcNow) };
        if (c.SampleType is not null) updates.Add(u.Set(x => x.SampleType, c.SampleType));
        if (c.CollectedAt is not null) updates.Add(u.Set(x => x.CollectedAt, c.CollectedAt.Value));
        if (c.VolumeMl is not null) updates.Add(u.Set(x => x.VolumeMl, c.VolumeMl.Value));
        if (c.Status is not null) updates.Add(u.Set(x => x.StatusString, c.Status));
        if (c.StorageLocation is not null) updates.Add(u.Set(x => x.StorageLocation, c.StorageLocation));
        if (c.Notes is not null) updates.Add(u.Set(x => x.Notes, c.Notes));
        if (c.AssignedTechnicianId is not null) updates.Add(u.Set(x => x.AssignedTechnicianId, c.AssignedTechnicianId));
        if (c.RejectionReason is not null) updates.Add(u.Set(x => x.RejectionReason, c.RejectionReason));
        if (c.ReceivedAt is not null) updates.Add(u.Set(x => x.ReceivedAt, c.ReceivedAt));
        if (c.ReceivedBy is not null) updates.Add(u.Set(x => x.ReceivedBy, c.ReceivedBy));
        if (c.RejectedAt is not null) updates.Add(u.Set(x => x.RejectedAt, c.RejectedAt));
        if (history is not null) updates.Add(u.Push(x => x.StatusHistory, history));

        var result = await _samples.UpdateOneAsync(ByIdOrBarcode(id), u.Combine(updates), cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(string id, CancellationToken ct = default)
    {
        var u = Builders<Sample>.Update;
        var result = await _samples.UpdateOneAsync(ByIdOrBarcode(id),
            u.Set(x => x.IsDeleted, true).Set(x => x.UpdatedAt, DateTime.UtcNow), cancellationToken: ct);
        return result.MatchedCount > 0;
    }
}
