using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for biological reference interval configuration and auto-evaluation (UC57).
/// </summary>
public interface IReferenceIntervalService
{
    Task<PagedResult<ReferenceIntervalResponseDto>> GetIntervalsPagedAsync(
        ReferenceIntervalQueryParameters parameters,
        CancellationToken ct = default);

    Task<List<ReferenceIntervalResponseDto>> GetIntervalsByCriterionCodeAsync(
        string criterionCode,
        bool activeOnly = true,
        CancellationToken ct = default);

    Task<ReferenceIntervalResponseDto?> GetIntervalByIdAsync(
        string id,
        CancellationToken ct = default);

    Task<(bool Success, string Message, ReferenceIntervalResponseDto? Data)> CreateIntervalAsync(
        CreateReferenceIntervalDto dto,
        CancellationToken ct = default);

    Task<(bool Success, string Message, ReferenceIntervalResponseDto? Data)> UpdateIntervalAsync(
        string id,
        UpdateReferenceIntervalDto dto,
        CancellationToken ct = default);

    Task<(bool Success, string Message)> DeactivateIntervalAsync(
        string id,
        CancellationToken ct = default);

    Task<EvaluateTestResultResponseDto> EvaluateResultAsync(
        EvaluateTestResultRequestDto request,
        CancellationToken ct = default);
}
