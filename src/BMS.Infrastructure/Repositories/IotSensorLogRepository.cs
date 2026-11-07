using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB and in-memory implementation of <see cref="IIotSensorLogRepository"/> (UC65, UC66, UC69).
/// Persists telemetry time-series into <c>iot_temperature_logs</c> and connection outages into <c>iot_connection_outages</c>.
/// </summary>
public class IotSensorLogRepository : IIotSensorLogRepository
{
    public const string LogsCollectionName = "iot_temperature_logs";
    public const string OutagesCollectionName = "iot_connection_outages";

    private readonly IMongoCollection<IotSensorLog>? _logsCollection;
    private readonly IMongoCollection<IotConnectionOutage>? _outagesCollection;
    private readonly BmsDbContext _dbContext;

    public IotSensorLogRepository(IMongoDatabase? database, BmsDbContext dbContext)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _logsCollection = database.GetCollection<IotSensorLog>(LogsCollectionName);
                _outagesCollection = database.GetCollection<IotConnectionOutage>(OutagesCollectionName);
            }
            catch
            {
                // Fallback to in-memory BmsDbContext if MongoDB is unavailable
            }
        }
    }

    public static IEnumerable<CreateIndexModel<IotSensorLog>> LogIndexes() =>
    [
        // Compound index supports fast device time-series range queries sorted by recorded_at descending
        new CreateIndexModel<IotSensorLog>(
            Builders<IotSensorLog>.IndexKeys.Ascending(x => x.DeviceId).Descending(x => x.RecordedAt),
            new CreateIndexOptions { Name = "ix_iot_logs_device_recorded_at", Background = true })
    ];

    public async Task InsertLogAsync(IotSensorLog log, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(log.Id))
        {
            log.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        }

        if (_logsCollection != null)
        {
            try
            {
                await _logsCollection.InsertOneAsync(log, cancellationToken: ct);
            }
            catch
            {
                // Fallback to in-memory store
            }
        }

        lock (_dbContext.IotSensorLogs)
        {
            _dbContext.IotSensorLogs.Add(log);
            // Bound in-memory cache to prevent unbounded memory growth while keeping full history in MongoDB
            if (_dbContext.IotSensorLogs.Count > 2000)
            {
                _dbContext.IotSensorLogs.RemoveRange(0, _dbContext.IotSensorLogs.Count - 2000);
            }
        }
    }

    public async Task InsertLogsBatchAsync(IEnumerable<IotSensorLog> logs, CancellationToken ct = default)
    {
        var list = logs?.ToList() ?? new List<IotSensorLog>();
        if (list.Count == 0) return;

        foreach (var item in list)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
            }
        }

        if (_logsCollection != null)
        {
            try
            {
                await _logsCollection.InsertManyAsync(list, new InsertManyOptions { IsOrdered = false }, ct);
            }
            catch
            {
                // Fallback to in-memory store
            }
        }

        lock (_dbContext.IotSensorLogs)
        {
            _dbContext.IotSensorLogs.AddRange(list);
            if (_dbContext.IotSensorLogs.Count > 2000)
            {
                _dbContext.IotSensorLogs.RemoveRange(0, _dbContext.IotSensorLogs.Count - 2000);
            }
        }
    }

    public async Task<List<IotSensorLog>> GetLogsAsync(
        string? deviceId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? statusFilter = null,
        bool? isBufferedOnly = null,
        string? outageId = null,
        string? searchTerm = null,
        int maxLimit = 500,
        CancellationToken ct = default)
    {
        List<IotSensorLog> sourceLogs = new();

        if (_logsCollection != null)
        {
            try
            {
                var fb = Builders<IotSensorLog>.Filter;
                var filter = fb.Empty;

                if (!string.IsNullOrWhiteSpace(deviceId) && !deviceId.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    filter &= fb.Eq(x => x.DeviceId, deviceId);
                }

                if (fromDate.HasValue)
                {
                    filter &= fb.Gte(x => x.RecordedAt, fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    filter &= fb.Lte(x => x.RecordedAt, toDate.Value);
                }

                if (isBufferedOnly.HasValue)
                {
                    filter &= fb.Eq(x => x.IsBuffered, isBufferedOnly.Value);
                }

                if (!string.IsNullOrWhiteSpace(outageId))
                {
                    filter &= fb.Eq(x => x.OutageId, outageId);
                }

                var mongoLogs = await _logsCollection
                    .Find(filter)
                    .SortByDescending(x => x.RecordedAt)
                    .Limit(Math.Clamp(maxLimit, 1, 2000))
                    .ToListAsync(ct);

                if (mongoLogs != null && mongoLogs.Count > 0)
                {
                    sourceLogs = mongoLogs;
                }
            }
            catch
            {
                // Fallback to in-memory
            }
        }

        if (sourceLogs.Count == 0)
        {
            lock (_dbContext.IotSensorLogs)
            {
                sourceLogs = _dbContext.IotSensorLogs.ToList();
            }
        }

        var query = sourceLogs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(deviceId) && !deviceId.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        }

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.RecordedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x => x.RecordedAt <= toDate.Value);
        }

        if (isBufferedOnly.HasValue)
        {
            query = query.Where(x => x.IsBuffered == isBufferedOnly.Value);
        }

        if (!string.IsNullOrWhiteSpace(outageId))
        {
            query = query.Where(x => string.Equals(x.OutageId, outageId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (statusFilter.Equals("Violation", StringComparison.OrdinalIgnoreCase) || statusFilter.Equals("Alert", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => !x.Status.Equals("Normal", StringComparison.OrdinalIgnoreCase));
            }
            else if (statusFilter.Equals("Buffered", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.IsBuffered);
            }
            else
            {
                query = query.Where(x => x.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var q = searchTerm.Trim();
            query = query.Where(x =>
                x.DeviceId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.DeviceName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Location.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Status.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderByDescending(x => x.RecordedAt)
            .Take(Math.Clamp(maxLimit, 1, 2000))
            .ToList();
    }

    public async Task<IotSensorLog?> GetLatestLogByDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        if (_logsCollection != null)
        {
            try
            {
                var latest = await _logsCollection
                    .Find(x => x.DeviceId == deviceId)
                    .SortByDescending(x => x.RecordedAt)
                    .FirstOrDefaultAsync(ct);
                if (latest != null) return latest;
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.IotSensorLogs)
        {
            return _dbContext.IotSensorLogs
                .Where(x => x.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.RecordedAt)
                .FirstOrDefault();
        }
    }

    public async Task<List<IotConnectionOutage>> GetOutagesAsync(string? deviceId = null, int maxLimit = 100, CancellationToken ct = default)
    {
        List<IotConnectionOutage> list = new();

        if (_outagesCollection != null)
        {
            try
            {
                var fb = Builders<IotConnectionOutage>.Filter;
                var filter = string.IsNullOrWhiteSpace(deviceId) || deviceId.Equals("All", StringComparison.OrdinalIgnoreCase)
                    ? fb.Empty
                    : fb.Eq(x => x.DeviceId, deviceId);

                var mongoList = await _outagesCollection
                    .Find(filter)
                    .SortByDescending(x => x.DisconnectedAt)
                    .Limit(Math.Clamp(maxLimit, 1, 500))
                    .ToListAsync(ct);

                if (mongoList != null && mongoList.Count > 0)
                {
                    list = mongoList;
                }
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.IotConnectionOutages)
        {
            foreach (var memOutage in _dbContext.IotConnectionOutages)
            {
                var idx = list.FindIndex(x => x.Id == memOutage.Id);
                if (idx >= 0) list[idx] = memOutage;
                else list.Add(memOutage);
            }
        }

        if (!string.IsNullOrWhiteSpace(deviceId) && !deviceId.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            list = list.Where(x => x.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return list.OrderByDescending(x => x.DisconnectedAt).Take(maxLimit).ToList();
    }

    public async Task<IotConnectionOutage?> GetActiveOutageAsync(string deviceId, CancellationToken ct = default)
    {
        if (_outagesCollection != null)
        {
            try
            {
                var active = await _outagesCollection
                    .Find(x => x.DeviceId == deviceId && x.Status == "Disconnected")
                    .SortByDescending(x => x.DisconnectedAt)
                    .FirstOrDefaultAsync(ct);
                if (active != null) return active;
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.IotConnectionOutages)
        {
            return _dbContext.IotConnectionOutages
                .Where(x => x.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase) && x.Status == "Disconnected")
                .OrderByDescending(x => x.DisconnectedAt)
                .FirstOrDefault();
        }
    }

    public async Task UpsertOutageAsync(IotConnectionOutage outage, CancellationToken ct = default)
    {
        if (_outagesCollection != null)
        {
            try
            {
                await _outagesCollection.ReplaceOneAsync(
                    x => x.Id == outage.Id,
                    outage,
                    new ReplaceOptions { IsUpsert = true },
                    ct);
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.IotConnectionOutages)
        {
            var idx = _dbContext.IotConnectionOutages.FindIndex(x => x.Id == outage.Id);
            if (idx >= 0) _dbContext.IotConnectionOutages[idx] = outage;
            else _dbContext.IotConnectionOutages.Add(outage);
        }
    }
}
