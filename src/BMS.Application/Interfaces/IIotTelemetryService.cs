using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract for IoT temperature &amp; humidity collection (UC65, UC66),
/// connection outage tracking, offline buffer synchronization, and historical log analytics (UC69).
/// </summary>
public interface IIotTelemetryService
{
    Task<IotTelemetryIngestResponseDto> IngestTelemetryAsync(IngestIotTelemetryRequestDto request, CancellationToken ct = default);
    Task<IotMonitoringDashboardDto> GetMonitoringDashboardAsync(IotSensorLogQueryDto query, CancellationToken ct = default);
    Task<IotConnectionStatusDto> GetConnectionStatusAsync(string? deviceId = null, CancellationToken ct = default);
    Task<List<IotConnectionOutageDto>> GetOutagesAsync(string? deviceId = null, CancellationToken ct = default);
    Task<IotConnectionStatusDto> ForceDisconnectStateAsync(string deviceId, string? reason = null, CancellationToken ct = default);
    Task<IotTelemetryIngestResponseDto> SimulateReconnectWithBufferedDataAsync(SimulateReconnectRequestDto request, CancellationToken ct = default);
    Task<IotTelemetryIngestResponseDto> ReevaluateThresholdsAsync(string deviceId = "BMS-IOT-001", CancellationToken ct = default);
}
