using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <inheritdoc />
public sealed class TestOrderRepository(IMongoDatabase database) : ITestOrderRepository
{
    /// <summary>MongoDB collection name.</summary>
    public const string CollectionName = "test_orders";

    private readonly IMongoCollection<TestOrder> _orders = database.GetCollection<TestOrder>(CollectionName);
    private static readonly FilterDefinitionBuilder<TestOrder> F = Builders<TestOrder>.Filter;

    /// <summary>Unique order number; list filters by patient, doctor, status (INV-5).</summary>
    public static IEnumerable<CreateIndexModel<TestOrder>> Indexes()
    {
        var k = Builders<TestOrder>.IndexKeys;
        yield return new(k.Ascending(o => o.OrderNumber), new CreateIndexOptions<TestOrder>
        {
            Name = "ux_test_orders_number",
            Unique = true,
            PartialFilterExpression = F.Type(o => o.OrderNumber, MongoDB.Bson.BsonType.String)
        });
        yield return new(k.Ascending(o => o.PatientId).Descending(o => o.CreatedAt), new CreateIndexOptions { Name = "ix_test_orders_patient_created" });
        yield return new(k.Ascending(o => o.DoctorId).Descending(o => o.CreatedAt), new CreateIndexOptions { Name = "ix_test_orders_doctor_created" });
        yield return new(k.Ascending(o => o.Status).Descending(o => o.CreatedAt), new CreateIndexOptions { Name = "ix_test_orders_status_created" });
    }

    /// <inheritdoc />
    public async Task<TestOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _orders.Find(o => o.Id == id).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<TestOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default) =>
        await _orders.Find(o => o.OrderNumber == orderNumber).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TestOrder>> GetAllAsync(string? patientId, string? doctorId, TestOrderStatus? status,
        int limit = 200, CancellationToken ct = default)
    {
        var filter = F.Empty;
        if (!string.IsNullOrWhiteSpace(patientId)) filter &= F.Eq(o => o.PatientId, patientId);
        if (!string.IsNullOrWhiteSpace(doctorId)) filter &= F.Eq(o => o.DoctorId, doctorId);
        if (status.HasValue) filter &= F.Eq(o => o.Status, status.Value);
        return await _orders.Find(filter).SortByDescending(o => o.CreatedAt).Limit(Math.Clamp(limit, 1, 500)).ToListAsync(ct);
    }

    /// <inheritdoc />
    public Task InsertAsync(TestOrder order, CancellationToken ct = default) =>
        _orders.InsertOneAsync(order, cancellationToken: ct);

    /// <inheritdoc />
    public async Task<TestOrder?> TryTransitionAsync(string id, IReadOnlyCollection<TestOrderStatus> from, TestOrderStatus to,
        string actorUserId, DateTime nowUtc, string? note, CancellationToken ct = default)
    {
        var update = Builders<TestOrder>.Update
            .Set(o => o.Status, to)
            .Set(o => o.UpdatedAt, nowUtc)
            .Set(o => o.StatusChangedBy, actorUserId);
        if (!string.IsNullOrWhiteSpace(note)) update = update.Set(o => o.StatusNote, note);
        if (to == TestOrderStatus.PaidPendingSample) update = update.Set(o => o.PaidAt, nowUtc);
        return await _orders.FindOneAndUpdateAsync(F.Eq(o => o.Id, id) & F.In(o => o.Status, from), update,
            new FindOneAndUpdateOptions<TestOrder> { ReturnDocument = ReturnDocument.After }, ct);
    }

    /// <inheritdoc />
    public async Task<TestOrder?> TrySubmitDraftAsync(string id, string doctorId, IReadOnlyList<string> icd10Codes, bool paymentDeferred,
        DateTime nowUtc, CancellationToken ct = default) =>
        await _orders.FindOneAndUpdateAsync(
            F.Eq(o => o.Id, id) & F.Eq(o => o.Status, TestOrderStatus.Draft) & F.Eq(o => o.DoctorId, doctorId),
            Builders<TestOrder>.Update
                .Set(o => o.Status, TestOrderStatus.AwaitingPayment)
                .Set(o => o.Icd10Codes, icd10Codes.ToList())
                .Set(o => o.PaymentDeferred, paymentDeferred)
                .Set(o => o.UpdatedAt, nowUtc)
                .Set(o => o.StatusChangedBy, doctorId),
            new FindOneAndUpdateOptions<TestOrder> { ReturnDocument = ReturnDocument.After }, ct);

    /// <inheritdoc />
    public async Task<TestOrder?> TryMarkPaidAsync(string id, string actorUserId, DateTime nowUtc, CancellationToken ct = default)
    {
        var options = new FindOneAndUpdateOptions<TestOrder> { ReturnDocument = ReturnDocument.After };
        var unpaid = F.Eq(o => o.Id, id) & F.Eq(o => o.PaidAt, null);
        var paid = await _orders.FindOneAndUpdateAsync(
            unpaid & F.In(o => o.Status, new[] { TestOrderStatus.AwaitingPayment, TestOrderStatus.Ordered }),
            Builders<TestOrder>.Update.Set(o => o.Status, TestOrderStatus.PaidPendingSample).Set(o => o.PaidAt, nowUtc)
                .Set(o => o.UpdatedAt, nowUtc).Set(o => o.StatusChangedBy, actorUserId), options, ct);
        if (paid != null) return paid;
        // WHY a second conditional update: a STAT order may already be sampled (post-payment); only the
        // payment timestamp changes, the clinical status stays.
        return await _orders.FindOneAndUpdateAsync(
            unpaid & F.Eq(o => o.PaymentDeferred, true)
                & F.In(o => o.Status, new[] { TestOrderStatus.SampleCollected, TestOrderStatus.InAnalysis, TestOrderStatus.Completed }),
            Builders<TestOrder>.Update.Set(o => o.PaidAt, nowUtc).Set(o => o.UpdatedAt, nowUtc), options, ct);
    }
}
