using BMS.Core.Entities;

namespace BMS.Application.Services;

/// <summary>
/// Who is called next at the reception counter (BA:L33, TIEU-CHI Bước 1 "Hàng chờ", SMK-LT-03).
/// </summary>
/// <remarks>
/// Rules: STAT first; otherwise policy-priority (Urgent) and Routine alternate 1:1, starting with priority;
/// within each group first come first served (lowest sequence). If one group is empty the other is served.
/// Pure function so the rule is unit-checkable without a database.
/// </remarks>
public static class QueueOrdering
{
    /// <summary>Picks the next ticket to call.</summary>
    /// <param name="waiting">Tickets currently Waiting (any order).</param>
    /// <param name="lastCalledNonStat">Priority of the last non-STAT ticket called today, or null if none.</param>
    /// <returns>The ticket to call, or null when nobody is waiting.</returns>
    public static QueueTicket? PickNext(IEnumerable<QueueTicket> waiting, QueuePriority? lastCalledNonStat)
    {
        var ordered = waiting.Where(t => t.Status == QueueTicketStatus.Waiting).OrderBy(t => t.Sequence).ToList();
        var stat = ordered.FirstOrDefault(t => t.Priority == QueuePriority.STAT);
        if (stat != null) return stat;

        var urgent = ordered.FirstOrDefault(t => t.Priority == QueuePriority.Urgent);
        var routine = ordered.FirstOrDefault(t => t.Priority == QueuePriority.Routine);
        var preferRoutine = lastCalledNonStat == QueuePriority.Urgent;
        return preferRoutine ? routine ?? urgent : urgent ?? routine;
    }
}
