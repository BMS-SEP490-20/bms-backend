using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <inheritdoc />
public sealed class QueueRepository(IMongoDatabase database) : IQueueRepository
{
    /// <summary>MongoDB collection name.</summary>
    public const string CollectionName = "queue_tickets";

    private readonly IMongoCollection<QueueTicket> _tickets = database.GetCollection<QueueTicket>(CollectionName);

    /// <summary>Indexes: unique (queueDate, ticketNumber) for new tickets; (queueDate, status, sequence) for the board.</summary>
    public static IEnumerable<CreateIndexModel<QueueTicket>> Indexes()
    {
        var k = Builders<QueueTicket>.IndexKeys;
        yield return new CreateIndexModel<QueueTicket>(
            k.Ascending(t => t.QueueDate).Ascending(t => t.TicketNumber),
            new CreateIndexOptions<QueueTicket>
            {
                Name = "ux_queue_date_ticket",
                Unique = true,
                // WHY partial: legacy tickets have no queueDate and may repeat numbers across days.
                PartialFilterExpression = Builders<QueueTicket>.Filter.Exists(t => t.QueueDate)
            });
        yield return new CreateIndexModel<QueueTicket>(
            k.Ascending(t => t.QueueDate).Ascending(t => t.Status).Ascending(t => t.Sequence),
            new CreateIndexOptions { Name = "ix_queue_date_status_seq" });
        yield return new CreateIndexModel<QueueTicket>(
            k.Ascending(t => t.QueueDate).Descending(t => t.CallOrder),
            new CreateIndexOptions { Name = "ix_queue_date_callorder" });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueueTicket>> GetByDateAsync(string queueDate, CancellationToken ct = default) =>
        await _tickets.Find(t => t.QueueDate == queueDate).SortBy(t => t.Sequence).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<QueueTicket?> GetByIdAsync(string id, string queueDate, CancellationToken ct = default) =>
        await _tickets.Find(t => t.Id == id || (t.QueueDate == queueDate && t.TicketNumber == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task InsertAsync(QueueTicket ticket, CancellationToken ct = default) =>
        _tickets.InsertOneAsync(ticket, cancellationToken: ct);

    /// <inheritdoc />
    public async Task<QueueTicket?> TryTransitionAsync(string id, IReadOnlyCollection<QueueTicketStatus> from,
        QueueTicketStatus to, string? counterName, DateTime nowUtc, long? callOrder = null, CancellationToken ct = default)
    {
        var f = Builders<QueueTicket>.Filter;
        var filter = f.Eq(t => t.Id, id) & f.In(t => t.Status, from);
        var update = Builders<QueueTicket>.Update.Set(t => t.Status, to);
        if (!string.IsNullOrWhiteSpace(counterName)) update = update.Set(t => t.CounterName, counterName);
        if (to == QueueTicketStatus.Called) update = update.Set(t => t.CalledAt, nowUtc);
        if (callOrder.HasValue) update = update.Set(t => t.CallOrder, callOrder);
        if (to is QueueTicketStatus.Completed or QueueTicketStatus.Skipped) update = update.Set(t => t.CompletedAt, nowUtc);
        return await _tickets.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<QueueTicket> { ReturnDocument = ReturnDocument.After }, ct);
    }

    /// <inheritdoc />
    public async Task<QueuePriority?> GetLastCalledNonStatPriorityAsync(string queueDate, CancellationToken ct = default)
    {
        var f = Builders<QueueTicket>.Filter;
        var last = await _tickets
            .Find(f.Eq(t => t.QueueDate, queueDate) & f.Ne(t => t.CallOrder, null) & f.Ne(t => t.Priority, QueuePriority.STAT))
            .SortByDescending(t => t.CallOrder)
            .Project(t => (QueuePriority?)t.Priority)
            .FirstOrDefaultAsync(ct);
        return last;
    }
}
