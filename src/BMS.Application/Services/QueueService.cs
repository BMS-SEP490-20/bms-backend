using BMS.Application.DTOs.Reception;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using BMS.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <summary>
/// Reception queue (UC27/UC28): issues daily numbers Q-001..Q-999 from an atomic counter, calls the next
/// patient by the STAT / 1:1 priority rule and moves tickets through their lifecycle with conditional updates.
/// </summary>
/// <remarks>WHY rewritten (Đợt 3): the old service read "max ticket + 1" (duplicates under parallel requests),
/// used the in-memory BmsDbContext and filled the board with placeholder staff names.</remarks>
public class QueueService(
    IQueueRepository repository,
    ISequenceGenerator sequences,
    TimeProvider clock,
    ILogger<QueueService> logger) : IQueueService
{
    /// <summary>Highest number of a day (BA:L33: after Q-999 the queue is closed, never Q-1000).</summary>
    public const int MaxTicketsPerDay = 999;

    // WHY 25: with N counters racing, a loser re-reads and retries; 25 rounds covers far more counters than the clinic has.
    private const int ClaimAttempts = 25;

    private static readonly IReadOnlyDictionary<QueueTicketStatus, QueueTicketStatus[]> AllowedFrom =
        new Dictionary<QueueTicketStatus, QueueTicketStatus[]>
        {
            [QueueTicketStatus.Called] = [QueueTicketStatus.Waiting, QueueTicketStatus.Called],
            [QueueTicketStatus.Collecting] = [QueueTicketStatus.Called],
            [QueueTicketStatus.Completed] = [QueueTicketStatus.Called, QueueTicketStatus.Collecting],
            [QueueTicketStatus.Skipped] = [QueueTicketStatus.Waiting, QueueTicketStatus.Called],
            [QueueTicketStatus.Waiting] = [QueueTicketStatus.Skipped],
        };

    /// <inheritdoc />
    public async Task<QueueBoardDto> GetLiveQueueBoardAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var tickets = await repository.GetByDateAsync(ClinicTime.LocalDateKey(now), ct);
        var active = tickets
            .Where(t => t.Status is QueueTicketStatus.Waiting or QueueTicketStatus.Called or QueueTicketStatus.Collecting)
            .Select(t => ToDto(t, now))
            .ToList();
        var lastSeq = tickets.Count == 0 ? 0 : tickets.Max(t => t.Sequence);
        var counters = active.Where(t => !string.IsNullOrWhiteSpace(t.CounterName))
            .GroupBy(t => t.CounterName!)
            .Select(g => new CounterStatusDto
            {
                CounterName = g.Key,
                StaffName = string.Empty,
                Status = "Active",
                Description = $"Đang phục vụ số {g.OrderByDescending(x => x.TicketNumber).First().TicketNumber}"
            })
            .ToList();

        return new QueueBoardDto
        {
            CurrentQueue = active,
            NextNumber = lastSeq >= MaxTicketsPerDay ? string.Empty : Format(lastSeq + 1),
            Stats = new QueueStatsDto
            {
                InQueueNow = active.Count,
                AvgWaitMinutes = active.Count == 0 ? 0 : (int)active.Average(x => x.WaitTimeMinutes),
                LongestWaitMinutes = active.Count == 0 ? 0 : active.Max(x => x.WaitTimeMinutes),
                ServedToday = tickets.Count(t => t.Status == QueueTicketStatus.Completed),
                CountersActive = counters.Count.ToString()
            },
            Counters = counters
        };
    }

    /// <inheritdoc />
    public async Task<QueueTicketDto> IssueTicketAsync(IssueTicketRequestDto request, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(request.Priority))
            throw new DomainValidationException("QUEUE_PRIORITY_INVALID", "Mức ưu tiên không hợp lệ.");

        var now = clock.GetUtcNow().UtcDateTime;
        var day = ClinicTime.LocalDateKey(now);
        var seq = await sequences.NextAsync($"queue:{day}", ct);
        if (seq > MaxTicketsPerDay) throw new QueueExhaustedException();

        var ticket = new QueueTicket
        {
            TicketNumber = Format(seq),
            QueueDate = day,
            Sequence = (int)seq,
            PatientId = request.PatientId?.Trim() ?? string.Empty,
            PatientName = request.PatientName?.Trim() ?? string.Empty,
            PatientCode = request.PatientCode?.Trim() ?? string.Empty,
            PackageName = request.PackageName?.Trim() ?? string.Empty,
            IsWalkIn = request.IsWalkIn,
            Priority = request.Priority,
            Status = QueueTicketStatus.Waiting,
            CheckedInAt = now
        };
        await repository.InsertAsync(ticket, ct);
        logger.LogInformation("Queue ticket {TicketNumber} issued ({Priority}) for {Patient}",
            ticket.TicketNumber, ticket.Priority, PiiMasker.MaskName(ticket.PatientName));
        return ToDto(ticket, now);
    }

    /// <inheritdoc />
    public async Task<QueueTicketDto?> CallNextAsync(string counterName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(counterName))
            throw new DomainValidationException("QUEUE_COUNTER_REQUIRED", "Vui lòng chọn quầy.");
        var now = clock.GetUtcNow().UtcDateTime;
        var day = ClinicTime.LocalDateKey(now);
        // WHY the loop: two counters may pick the same candidate; the conditional update lets only one win,
        // the loser re-reads and takes the next person.
        for (var attempt = 0; attempt < ClaimAttempts; attempt++)
        {
            var tickets = await repository.GetByDateAsync(day, ct);
            var last = await repository.GetLastCalledNonStatPriorityAsync(day, ct);
            var next = QueueOrdering.PickNext(tickets, last);
            if (next == null) return null;
            var order = await sequences.NextAsync($"queue-call:{day}", ct);
            var claimed = await repository.TryTransitionAsync(next.Id, [QueueTicketStatus.Waiting],
                QueueTicketStatus.Called, counterName.Trim(), now, order, ct);
            if (claimed != null) return ToDto(claimed, now);
        }
        throw new ConflictStateException("QUEUE_BUSY", "Hàng chờ đang được cập nhật, vui lòng thử lại.");
    }

    /// <inheritdoc />
    public async Task<bool> CallTicketAsync(string ticketId, string counterName, CancellationToken ct = default) =>
        await TransitionAsync(ticketId, QueueTicketStatus.Called, counterName, ct);

    /// <inheritdoc />
    public async Task<bool> UpdateTicketStatusAsync(string ticketId, UpdateTicketStatusRequestDto request, CancellationToken ct = default) =>
        await TransitionAsync(ticketId, request.Status, request.CounterName, ct);

    private async Task<bool> TransitionAsync(string ticketId, QueueTicketStatus to, string? counterName, CancellationToken ct)
    {
        if (!AllowedFrom.TryGetValue(to, out var from))
            throw new DomainValidationException("QUEUE_STATUS_INVALID", "Trạng thái số thứ tự không hợp lệ.");
        var now = clock.GetUtcNow().UtcDateTime;
        var ticket = await repository.GetByIdAsync(ticketId, ClinicTime.LocalDateKey(now), ct);
        if (ticket == null) return false;
        long? order = to == QueueTicketStatus.Called ? await sequences.NextAsync($"queue-call:{ClinicTime.LocalDateKey(now)}", ct) : null;
        var updated = await repository.TryTransitionAsync(ticket.Id, from, to, counterName, now, order, ct);
        if (updated == null)
            throw new ConflictStateException("QUEUE_INVALID_TRANSITION",
                $"Không thể chuyển số {ticket.TicketNumber} từ '{ticket.Status}' sang '{to}'.");
        return true;
    }

    /// <summary>Formats a sequence as Q-001..Q-999.</summary>
    public static string Format(long sequence) => $"Q-{sequence:D3}";

    private static QueueTicketDto ToDto(QueueTicket t, DateTime nowUtc) => new()
    {
        Id = t.Id,
        TicketNumber = t.TicketNumber,
        PatientName = t.PatientName,
        PatientCode = t.PatientCode,
        PackageName = t.PackageName,
        IsWalkIn = t.IsWalkIn,
        CheckedInTime = t.CheckedInAt.Add(ClinicTime.Offset).ToString("HH:mm"),
        WaitTimeMinutes = Math.Max(0, (int)(nowUtc - t.CheckedInAt).TotalMinutes),
        Priority = t.Priority.ToString(),
        Status = t.Status.ToString(),
        CounterName = t.CounterName
    };
}
