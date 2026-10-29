using BMS.Application.DTOs.Reception;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract for reception queue and ticketing workflows (UC27).
/// </summary>
public interface IQueueService
{
    Task<QueueBoardDto> GetLiveQueueBoardAsync(CancellationToken ct = default);
    Task<QueueTicketDto> IssueTicketAsync(IssueTicketRequestDto request, CancellationToken ct = default);
    /// <summary>Calls the next patient by the STAT / 1:1 priority rule; null when nobody is waiting.</summary>
    Task<QueueTicketDto?> CallNextAsync(string counterName, CancellationToken ct = default);
    Task<bool> CallTicketAsync(string ticketId, string counterName, CancellationToken ct = default);
    Task<bool> UpdateTicketStatusAsync(string ticketId, UpdateTicketStatusRequestDto request, CancellationToken ct = default);
}
