using BMS.Application.DTOs.Reception;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Services;

/// <summary>
/// Business logic service for Reception Queue Management & Ticket Issuance (UC27).
/// Computes queue statistics, issues sequential queue numbers, and manages counter calling.
/// </summary>
public class QueueService : IQueueService
{
    private readonly IQueueRepository _repository;
    private readonly BmsDbContext _dbContext;
    private readonly IMongoCollection<User>? _usersCol;
    private readonly ILogger<QueueService> _logger;

    public QueueService(
        IQueueRepository repository,
        BmsDbContext dbContext,
        ILogger<QueueService> logger,
        IMongoDatabase? database = null)
    {
        _repository = repository;
        _dbContext = dbContext;
        _logger = logger;
        if (database != null)
        {
            try
            {
                _usersCol = database.GetCollection<User>("accounts");
            }
            catch
            {
                _usersCol = null;
            }
        }
    }

    public async Task<QueueBoardDto> GetLiveQueueBoardAsync(CancellationToken ct = default)
    {
        var rawTickets = await _repository.GetTodayQueueAsync(ct);
        var nextNumber = await _repository.GetNextTicketNumberAsync(ct);

        var now = DateTime.UtcNow;

        var currentQueue = rawTickets
            .Where(t => t.Status == QueueTicketStatus.Waiting || t.Status == QueueTicketStatus.Called || t.Status == QueueTicketStatus.Collecting)
            .Select(t => new QueueTicketDto
            {
                Id = t.Id,
                TicketNumber = t.TicketNumber,
                PatientName = t.PatientName,
                PatientCode = t.PatientCode,
                PackageName = t.PackageName,
                IsWalkIn = t.IsWalkIn,
                CheckedInTime = t.CheckedInAt.ToString("HH:mm"),
                WaitTimeMinutes = Math.Max(1, (int)(now - t.CheckedInAt).TotalMinutes),
                Priority = t.Priority.ToString(),
                Status = t.Status.ToString(),
                CounterName = t.CounterName
            })
            .ToList();

        int inQueueNow = currentQueue.Count;
        int avgWait = currentQueue.Count > 0 ? (int)currentQueue.Average(x => x.WaitTimeMinutes) : 0;
        int longestWait = currentQueue.Count > 0 ? currentQueue.Max(x => x.WaitTimeMinutes) : 0;
        int servedToday = rawTickets.Count(t => t.Status == QueueTicketStatus.Completed);

        // Load active reception staff dynamically from database
        var staffList = new List<string>();
        if (_usersCol != null)
        {
            try
            {
                var dbStaff = await _usersCol.Find(u => u.Role == UserRole.Receptionist && u.IsActive).ToListAsync(ct);
                staffList.AddRange(dbStaff.Select(u => u.FullName));
            }
            catch { }
        }

        if (staffList.Count == 0)
        {
            staffList.AddRange(_dbContext.Users.Where(u => u.Role == UserRole.Receptionist && u.IsActive).Select(u => u.FullName));
        }

        if (staffList.Count == 0)
        {
            staffList.Add("Lễ tân quầy");
        }

        var counters = new List<CounterStatusDto>();
        for (int i = 0; i < staffList.Count; i++)
        {
            var counterName = $"Quầy {i + 1}";
            var staffName = staffList[i];
            var activeTicket = currentQueue.FirstOrDefault(x => x.CounterName == counterName || x.CounterName == $"Counter {i + 1}");

            counters.Add(new CounterStatusDto
            {
                CounterName = counterName,
                StaffName = staffName,
                Status = "Active",
                Description = activeTicket != null
                    ? $"Đang phục vụ số {activeTicket.TicketNumber}"
                    : "Đang sẵn sàng"
            });
        }

        return new QueueBoardDto
        {
            CurrentQueue = currentQueue,
            NextNumber = nextNumber,
            Stats = new QueueStatsDto
            {
                InQueueNow = inQueueNow,
                AvgWaitMinutes = avgWait,
                LongestWaitMinutes = longestWait,
                ServedToday = servedToday,
                CountersActive = $"{counters.Count(c => c.Status == "Active")} / {counters.Count}"
            },
            Counters = counters
        };
    }

    public async Task<QueueTicketDto> IssueTicketAsync(IssueTicketRequestDto request, CancellationToken ct = default)
    {
        var ticketNumber = await _repository.GetNextTicketNumberAsync(ct);
        var now = DateTime.UtcNow;

        var ticket = new QueueTicket
        {
            TicketNumber = ticketNumber,
            PatientId = request.PatientId ?? string.Empty,
            PatientName = request.PatientName ?? string.Empty,
            PatientCode = request.PatientCode ?? string.Empty,
            PackageName = request.PackageName ?? string.Empty,
            IsWalkIn = request.IsWalkIn,
            Priority = request.Priority,
            Status = QueueTicketStatus.Waiting,
            CheckedInAt = now,
            EstimatedWaitMinutes = 0
        };

        await _repository.InsertAsync(ticket, ct);
        _logger.LogInformation("[UC27 Queue] Issued ticket {TicketNumber} for patient {Patient}", ticketNumber, ticket.PatientName);

        return new QueueTicketDto
        {
            Id = ticket.Id,
            TicketNumber = ticket.TicketNumber,
            PatientName = ticket.PatientName,
            PatientCode = ticket.PatientCode,
            PackageName = ticket.PackageName,
            IsWalkIn = ticket.IsWalkIn,
            CheckedInTime = ticket.CheckedInAt.ToString("HH:mm"),
            WaitTimeMinutes = 1,
            Priority = ticket.Priority.ToString(),
            Status = ticket.Status.ToString()
        };
    }

    public async Task<bool> CallTicketAsync(string ticketId, string counterName, CancellationToken ct = default)
    {
        var ticket = await _repository.GetByIdAsync(ticketId, ct);
        if (ticket == null) return false;

        await _repository.UpdateStatusAsync(ticketId, QueueTicketStatus.Called, counterName, ct);
        _logger.LogInformation("[UC27 Queue] Called ticket {Number} to {Counter}", ticket.TicketNumber, counterName);
        return true;
    }

    public async Task<bool> UpdateTicketStatusAsync(string ticketId, UpdateTicketStatusRequestDto request, CancellationToken ct = default)
    {
        var ticket = await _repository.GetByIdAsync(ticketId, ct);
        if (ticket == null) return false;

        await _repository.UpdateStatusAsync(ticketId, request.Status, request.CounterName, ct);
        return true;
    }
}
