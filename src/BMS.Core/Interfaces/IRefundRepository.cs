using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>Refunds of cancelled paid orders (<c>refunds</c>); unique index on the order = at most one refund per order.</summary>
public interface IRefundRepository
{
    /// <summary>Inserts a refund; returns false when the order already has one (duplicate key).</summary>
    Task<bool> TryInsertAsync(Refund refund, CancellationToken ct = default);

    /// <summary>Refund by id or RF- code, or null.</summary>
    Task<Refund?> GetAsync(string idOrCode, CancellationToken ct = default);

    /// <summary>Refund of an order, or null.</summary>
    Task<Refund?> GetByOrderIdAsync(string orderId, CancellationToken ct = default);

    /// <summary>Newest first, optionally by status (max 200).</summary>
    Task<IReadOnlyList<Refund>> ListAsync(RefundStatus? status, CancellationToken ct = default);

    /// <summary>
    /// Atomically moves the refund from <paramref name="from"/> to <paramref name="to"/>, applying <paramref name="apply"/>'s
    /// field values (only non-null ones). Returns the updated refund, or null when it is no longer in <paramref name="from"/>.
    /// </summary>
    Task<Refund?> TryTransitionAsync(string id, RefundStatus from, RefundStatus to, RefundChanges apply, CancellationToken ct = default);
}

/// <summary>Fields set together with a refund transition; null = unchanged.</summary>
public sealed record RefundChanges(
    string? DecidedBy = null, DateTime? DecidedAt = null, string? DecisionNote = null,
    string? InvoiceId = null, PaymentMethod? PaymentMethod = null, RefundChannel? Channel = null,
    decimal? Amount = null, decimal? BhytReversalAmount = null, DateTime? BhytReversalPostedAt = null,
    string? PaidOutBy = null, DateTime? PaidOutAt = null);
