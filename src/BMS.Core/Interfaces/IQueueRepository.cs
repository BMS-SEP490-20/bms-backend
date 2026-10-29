using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Persistence for reception queue tickets (UC27/UC28), MongoDB <c>queue_tickets</c> only.
/// </summary>
/// <remarks>WHY: the former implementation mirrored every write into the in-memory BmsDbContext and silently
/// fell back to it when Mongo failed, so two app instances (or a restart) saw different queues.</remarks>
public interface IQueueRepository
{
    /// <summary>All tickets of one clinic-local day, ordered by sequence.</summary>
    Task<IReadOnlyList<QueueTicket>> GetByDateAsync(string queueDate, CancellationToken ct = default);

    /// <summary>Ticket by id or by ticket number of the given day.</summary>
    Task<QueueTicket?> GetByIdAsync(string id, string queueDate, CancellationToken ct = default);

    /// <summary>Inserts a new ticket; the unique index (queueDate, ticketNumber) rejects duplicates.</summary>
    Task InsertAsync(QueueTicket ticket, CancellationToken ct = default);

    /// <summary>
    /// Atomically moves a ticket from one of <paramref name="from"/> to <paramref name="to"/>
    /// (<c>findOneAndUpdate</c> with the current status in the filter).
    /// </summary>
    /// <returns>The updated ticket, or null when the ticket is missing or no longer in an allowed state.</returns>
    Task<QueueTicket?> TryTransitionAsync(string id, IReadOnlyCollection<QueueTicketStatus> from, QueueTicketStatus to,
        string? counterName, DateTime nowUtc, long? callOrder = null, CancellationToken ct = default);

    /// <summary>Priority of the most recent non-STAT ticket called today (for the 1:1 alternation), or null.</summary>
    Task<QueuePriority?> GetLastCalledNonStatPriorityAsync(string queueDate, CancellationToken ct = default);
}
