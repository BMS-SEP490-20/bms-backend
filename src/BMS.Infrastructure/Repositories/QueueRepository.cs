using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB repository implementation for Queue Tickets (UC27).
/// Synchronizes with BmsDbContext in-memory store for high-performance and resilient development.
/// </summary>
public class QueueRepository : IQueueRepository
{
    private readonly IMongoCollection<QueueTicket>? _collection;
    private readonly BmsDbContext _dbContext;

    public QueueRepository(BmsDbContext dbContext, IMongoDatabase? database = null)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _collection = database.GetCollection<QueueTicket>("queue_tickets");
                var indexKeys = Builders<QueueTicket>.IndexKeys
                    .Ascending(x => x.TicketNumber)
                    .Ascending(x => x.Status);
                _collection.Indexes.CreateOne(new CreateIndexModel<QueueTicket>(indexKeys));
            }
            catch
            {
                _collection = null;
            }
        }
    }

    public async Task<List<QueueTicket>> GetTodayQueueAsync(CancellationToken ct = default)
    {
        if (_collection != null)
        {
            try
            {
                var list = await _collection.Find(_ => true)
                    .SortBy(x => x.TicketNumber)
                    .ToListAsync(ct);
                if (list.Count > 0) return list;
            }
            catch { }
        }

        lock (_dbContext.QueueTickets)
        {
            return _dbContext.QueueTickets
                .OrderBy(x => x.TicketNumber)
                .ToList();
        }
    }

    public async Task<QueueTicket?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (_collection != null)
        {
            try
            {
                var item = await _collection.Find(x => x.Id == id || x.TicketNumber == id).FirstOrDefaultAsync(ct);
                if (item != null) return item;
            }
            catch { }
        }

        lock (_dbContext.QueueTickets)
        {
            return _dbContext.QueueTickets.FirstOrDefault(x => x.Id == id || x.TicketNumber == id);
        }
    }

    public async Task<string> GetNextTicketNumberAsync(CancellationToken ct = default)
    {
        var list = await GetTodayQueueAsync(ct);
        int maxSeq = 18; // base seed is Q-018
        foreach (var t in list)
        {
            if (t.TicketNumber.StartsWith("Q-") && int.TryParse(t.TicketNumber[2..], out var seq))
            {
                if (seq > maxSeq) maxSeq = seq;
            }
        }

        return $"Q-{(maxSeq + 1):D3}";
    }

    public async Task InsertAsync(QueueTicket ticket, CancellationToken ct = default)
    {
        lock (_dbContext.QueueTickets)
        {
            _dbContext.QueueTickets.Add(ticket);
        }

        if (_collection != null)
        {
            try
            {
                await _collection.InsertOneAsync(ticket, cancellationToken: ct);
            }
            catch { }
        }
    }

    public async Task UpdateStatusAsync(string id, QueueTicketStatus status, string? counterName = null, CancellationToken ct = default)
    {
        lock (_dbContext.QueueTickets)
        {
            var item = _dbContext.QueueTickets.FirstOrDefault(x => x.Id == id || x.TicketNumber == id);
            if (item != null)
            {
                item.Status = status;
                if (!string.IsNullOrEmpty(counterName)) item.CounterName = counterName;
                if (status == QueueTicketStatus.Called) item.CalledAt = DateTime.UtcNow;
            }
        }

        if (_collection != null)
        {
            try
            {
                var updateBuilder = Builders<QueueTicket>.Update.Set(x => x.Status, status);
                if (!string.IsNullOrEmpty(counterName))
                {
                    updateBuilder = updateBuilder.Set(x => x.CounterName, counterName);
                }
                if (status == QueueTicketStatus.Called)
                {
                    updateBuilder = updateBuilder.Set(x => x.CalledAt, DateTime.UtcNow);
                }

                await _collection.UpdateOneAsync(x => x.Id == id || x.TicketNumber == id, updateBuilder, cancellationToken: ct);
            }
            catch { }
        }
    }
}
