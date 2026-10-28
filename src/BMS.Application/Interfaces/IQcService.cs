using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.QualityControl;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for Quality Control management and Westgard Multi-Rule evaluation (UC73).
/// </summary>
public interface IQcService
{
    /// <summary>
    /// Retrieves list of QC lots with optional filtering.
    /// </summary>
    Task<List<QcLotDto>> GetLotsAsync(bool? activeOnly = null, string? analyte = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single QC lot by ID.
    /// </summary>
    Task<QcLotDto?> GetLotByIdAsync(string lotId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new QC lot with specified target mean and standard deviation.
    /// </summary>
    Task<QcLotDto> CreateLotAsync(CreateQcLotDto dto, CancellationToken ct = default);

    /// <summary>
    /// Updates parameters of an existing QC lot.
    /// </summary>
    Task<QcLotDto> UpdateLotAsync(string lotId, UpdateQcLotDto dto, CancellationToken ct = default);

    /// <summary>
    /// Toggles active/inactive status of a QC lot.
    /// </summary>
    Task<bool> ToggleLotStatusAsync(string lotId, CancellationToken ct = default);

    /// <summary>
    /// Records a new IQC measurement run, calculates Z-score, and executes Westgard multi-rule evaluation.
    /// </summary>
    Task<QcEvaluationResultDto> RecordRunAsync(RecordQcRunDto dto, CancellationToken ct = default);

    /// <summary>
    /// Retrieves Levey-Jennings chart data points and boundary markers (+/-1s, +/-2s, +/-3s) for a QC lot.
    /// </summary>
    Task<LeveyJenningsChartDto> GetLeveyJenningsDataAsync(string lotId, CancellationToken ct = default);

    /// <summary>
    /// Signs off on corrective action for an out-of-control QC run, unlocking the analyzer batch.
    /// </summary>
    Task<QcEvaluationResultDto> SignoffCorrectiveActionAsync(string runId, QcSignoffRequestDto dto, CancellationToken ct = default);

    /// <summary>
    /// Retrieves laboratory QC surveillance summary dashboard KPIs.
    /// </summary>
    Task<QcDashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves active out-of-control QC violations and Westgard warning alerts.
    /// </summary>
    Task<List<QcEvaluationResultDto>> GetActiveQcFlagsAsync(CancellationToken ct = default);
}
