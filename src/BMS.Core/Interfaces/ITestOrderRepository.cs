using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data access for clinical test orders (UC32) in the MongoDB <c>test_orders</c> collection.
/// </summary>
/// <remarks>WHY rewritten (Đợt 3): the old repository silently fell back to the in-memory BmsDbContext on any
/// Mongo error and numbered orders from that in-memory list (duplicates after a restart or in parallel).
/// Status changes are conditional (current status in the filter) so cancel/collect/pay races have one winner.</remarks>
public interface ITestOrderRepository
{
    /// <summary>Order by internal id, or null.</summary>
    Task<TestOrder?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Order by its ORD- number, or null.</summary>
    Task<TestOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default);

    /// <summary>Newest first, filtered on indexed fields; capped by <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<TestOrder>> GetAllAsync(string? patientId, string? doctorId, TestOrderStatus? status,
        int limit = 200, CancellationToken ct = default);

    /// <summary>Inserts a new order (OrderNumber already assigned; unique index rejects duplicates).</summary>
    Task InsertAsync(TestOrder order, CancellationToken ct = default);

    /// <summary>
    /// Atomically moves the order from one of <paramref name="from"/> to <paramref name="to"/>.
    /// </summary>
    /// <returns>The updated order, or null if it is missing or no longer in an allowed state.</returns>
    Task<TestOrder?> TryTransitionAsync(string id, IReadOnlyCollection<TestOrderStatus> from, TestOrderStatus to,
        string actorUserId, DateTime nowUtc, string? note, CancellationToken ct = default);
}
