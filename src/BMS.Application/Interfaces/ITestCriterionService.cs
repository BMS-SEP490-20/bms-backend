using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;

namespace BMS.Application.Interfaces;

/// <summary>
/// Business service contract for clinical laboratory test criteria lifecycle (UC18).
/// Enforces clinical range validations and ISO 15189 soft-deletion.
/// </summary>
public interface ITestCriterionService
{
    Task<PagedResult<TestCriterionResponseDto>> GetCriteriaPagedAsync(
        TestCriterionQueryParameters parameters,
        CancellationToken ct = default);

    Task<TestCriterionResponseDto?> GetCriterionByIdAsync(string id, CancellationToken ct = default);

    Task<(bool Success, string Message, TestCriterionResponseDto? Data)> CreateCriterionAsync(
        CreateTestCriterionDto dto,
        CancellationToken ct = default);

    Task<(bool Success, string Message, TestCriterionResponseDto? Data)> UpdateCriterionAsync(
        string id,
        UpdateTestCriterionDto dto,
        CancellationToken ct = default);

    Task<(bool Success, string Message)> DeactivateCriterionAsync(string id, CancellationToken ct = default);
}
