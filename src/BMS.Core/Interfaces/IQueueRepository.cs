using BMS.Core.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data repository contract for queue ticket management (UC27).
/// </summary>
public interface IQueueRepository
{
    Task<List<QueueTicket>> GetTodayQueueAsync(CancellationToken ct = default);
    Task<QueueTicket?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<string> GetNextTicketNumberAsync(CancellationToken ct = default);
    Task InsertAsync(QueueTicket ticket, CancellationToken ct = default);
    Task UpdateStatusAsync(string id, QueueTicketStatus status, string? counterName = null, CancellationToken ct = default);
}
