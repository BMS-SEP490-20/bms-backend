using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository contract for persisting and querying IoT temperature/humidity logs and connection outages (UC65, UC66, UC69).
/// </summary>
public interface IIotSensorLogRepository
{
    Task InsertLogAsync(IotSensorLog log, CancellationToken ct = default);
    Task InsertLogsBatchAsync(IEnumerable<IotSensorLog> logs, CancellationToken ct = default);
    Task<List<IotSensorLog>> GetLogsAsync(
        string? deviceId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? statusFilter = null,
        bool? isBufferedOnly = null,
        string? outageId = null,
        string? searchTerm = null,
        int maxLimit = 500,
        CancellationToken ct = default);
    Task<IotSensorLog?> GetLatestLogByDeviceAsync(string deviceId, CancellationToken ct = default);
    Task<List<IotConnectionOutage>> GetOutagesAsync(string? deviceId = null, int maxLimit = 100, CancellationToken ct = default);
    Task<IotConnectionOutage?> GetActiveOutageAsync(string deviceId, CancellationToken ct = default);
    Task UpsertOutageAsync(IotConnectionOutage outage, CancellationToken ct = default);
    Task UpsertDailySummaryAsync(IotDailySummary summary, CancellationToken ct = default);
    Task<List<IotDailySummary>> GetDailySummariesAsync(
        string? deviceId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int maxLimit = 365,
        CancellationToken ct = default);
    Task<IotMqttConfig?> GetMqttConfigAsync(CancellationToken ct = default);
    Task UpsertMqttConfigAsync(IotMqttConfig config, CancellationToken ct = default);
}
