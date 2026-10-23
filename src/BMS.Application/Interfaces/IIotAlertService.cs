using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for detecting IoT threshold violations, triggering alarms, and managing incident resolutions (UC68).
/// </summary>
public interface IIotAlertService
{
    /// <summary>
    /// Ingests sensor telemetry, compares against active device thresholds, and triggers alarms upon violation.
    /// </summary>
    Task<ThresholdEvaluationResultDto> EvaluateTelemetryAsync(TelemetryReadingDto reading, CancellationToken ct = default);

    /// <summary>
    /// Retrieves filtered list of IoT threshold violation alerts.
    /// </summary>
    Task<List<IotAlertDto>> GetAlertsAsync(IotAlertQueryParameters parameters, CancellationToken ct = default);

    /// <summary>
    /// Retrieves detailed alert record by ID.
    /// </summary>
    Task<IotAlertDto?> GetAlertByIdAsync(string alertId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves top-level surveillance metrics (active, critical, acknowledged, resolved).
    /// </summary>
    Task<IotAlertSummaryDto> GetAlertSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Acknowledges an active alert, recording operator ownership.
    /// </summary>
    Task<IotAlertDto> AcknowledgeAlertAsync(string alertId, AcknowledgeAlertRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Resolves an alert, documenting corrective actions taken to restore environmental parameters.
    /// </summary>
    Task<IotAlertDto> ResolveAlertAsync(string alertId, ResolveAlertRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// Triggers a simulated threshold violation alert for operational testing and staff drills.
    /// </summary>
    Task<IotAlertDto> TriggerSimulatedAlertAsync(SimulateAlertRequestDto request, CancellationToken ct = default);
}
