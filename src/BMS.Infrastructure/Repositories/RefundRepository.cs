using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <inheritdoc />
public sealed class RefundRepository(IMongoDatabase database) : IRefundRepository
{
    /// <summary>MongoDB collection name.</summary>
    public const string CollectionName = "refunds";

    private readonly IMongoCollection<Refund> _refunds = database.GetCollection<Refund>(CollectionName);
    private static readonly FilterDefinitionBuilder<Refund> F = Builders<Refund>.Filter;

    /// <summary>One refund per order (race-safe), unique RF- code, list by status.</summary>
    public static IEnumerable<CreateIndexModel<Refund>> Indexes()
    {
        var k = Builders<Refund>.IndexKeys;
        yield return new(k.Ascending(r => r.OrderId), new CreateIndexOptions { Name = "ux_refunds_order", Unique = true });
        yield return new(k.Ascending(r => r.RefundCode), new CreateIndexOptions { Name = "ux_refunds_code", Unique = true });
        yield return new(k.Ascending(r => r.Status).Descending(r => r.RequestedAt), new CreateIndexOptions { Name = "ix_refunds_status_requested" });
    }

    /// <inheritdoc />
    public async Task<bool> TryInsertAsync(Refund refund, CancellationToken ct = default)
    {
        try
        {
            await _refunds.InsertOneAsync(refund, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Refund?> GetAsync(string idOrCode, CancellationToken ct = default) =>
        await _refunds.Find(r => r.Id == idOrCode || r.RefundCode == idOrCode).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<Refund?> GetByOrderIdAsync(string orderId, CancellationToken ct = default) =>
        await _refunds.Find(r => r.OrderId == orderId).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Refund>> ListAsync(RefundStatus? status, CancellationToken ct = default) =>
        await _refunds.Find(status.HasValue ? F.Eq(r => r.Status, status.Value) : F.Empty)
            .SortByDescending(r => r.RequestedAt).Limit(200).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<Refund?> TryTransitionAsync(string id, RefundStatus from, RefundStatus to, RefundChanges c, CancellationToken ct = default)
    {
        var u = Builders<Refund>.Update;
        var updates = new List<UpdateDefinition<Refund>> { u.Set(r => r.Status, to) };
        if (c.DecidedBy != null) updates.Add(u.Set(r => r.DecidedBy, c.DecidedBy));
        if (c.DecidedAt != null) updates.Add(u.Set(r => r.DecidedAt, c.DecidedAt));
        if (c.DecisionNote != null) updates.Add(u.Set(r => r.DecisionNote, c.DecisionNote));
        if (c.InvoiceId != null) updates.Add(u.Set(r => r.InvoiceId, c.InvoiceId));
        if (c.PaymentMethod != null) updates.Add(u.Set(r => r.PaymentMethod, c.PaymentMethod));
        if (c.Channel != null) updates.Add(u.Set(r => r.Channel, c.Channel));
        if (c.Amount != null) updates.Add(u.Set(r => r.Amount, c.Amount.Value));
        if (c.BhytReversalAmount != null) updates.Add(u.Set(r => r.BhytReversalAmount, c.BhytReversalAmount.Value));
        if (c.BhytReversalPostedAt != null) updates.Add(u.Set(r => r.BhytReversalPostedAt, c.BhytReversalPostedAt));
        if (c.PaidOutBy != null) updates.Add(u.Set(r => r.PaidOutBy, c.PaidOutBy));
        if (c.PaidOutAt != null) updates.Add(u.Set(r => r.PaidOutAt, c.PaidOutAt));
        return await _refunds.FindOneAndUpdateAsync(F.Eq(r => r.Id, id) & F.Eq(r => r.Status, from), u.Combine(updates),
            new FindOneAndUpdateOptions<Refund> { ReturnDocument = ReturnDocument.After }, ct);
    }
}
