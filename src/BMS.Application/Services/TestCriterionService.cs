using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// Business service implementing laboratory test parameter management and clinical range validation (UC18).
/// Conforms to: ISO 15189:2022 & Decision 2429/QD-BYT.
/// </summary>
public class TestCriterionService : ITestCriterionService
{
    private readonly BMS.Core.Interfaces.ITestCriterionRepository _repository;

    public TestCriterionService(BMS.Core.Interfaces.ITestCriterionRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<TestCriterionResponseDto>> GetCriteriaPagedAsync(
        TestCriterionQueryParameters parameters,
        CancellationToken ct = default)
    {
        await _repository.EnsureDefaultCriteriaSeededAsync(ct);

        var (items, total) = await _repository.GetPagedAsync(
            parameters.Page,
            parameters.PageSize,
            parameters.Search,
            parameters.Category,
            parameters.IsActive,
            ct);

        return new PagedResult<TestCriterionResponseDto>
        {
            CurrentPage = parameters.Page,
            PageSize = parameters.PageSize,
            TotalCount = total,
            Items = items.Select(MapToDto).ToList()
        };
    }

    public async Task<TestCriterionResponseDto?> GetCriterionByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<(bool Success, string Message, TestCriterionResponseDto? Data)> CreateCriterionAsync(
        CreateTestCriterionDto dto,
        CancellationToken ct = default)
    {
        var normalizedCode = dto.CriterionCode.Trim().ToUpperInvariant();

        // Check duplicate criterion code
        var existing = await _repository.GetByCodeAsync(normalizedCode, ct);
        if (existing != null)
        {
            return (false, $"Test criterion code '{normalizedCode}' already exists in laboratory master data.", null);
        }

        // Validate clinical range logic
        var validation = ValidateClinicalRanges(dto.NormalMin, dto.NormalMax, dto.PanicLow, dto.PanicHigh);
        if (!validation.IsValid)
        {
            return (false, validation.Message, null);
        }

        var newEntity = new TestCriterionEntity
        {
            CriterionCode = normalizedCode,
            CriterionName = dto.CriterionName.Trim(),
            EnglishName = dto.EnglishName.Trim(),
            Category = dto.Category.Trim(),
            Unit = dto.Unit.Trim(),
            ValueType = dto.ValueType,
            NormalMin = dto.NormalMin,
            NormalMax = dto.NormalMax,
            NormalText = dto.NormalText?.Trim(),
            PanicLow = dto.PanicLow,
            PanicHigh = dto.PanicHigh,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.CreateAsync(newEntity, ct);
        return (true, "Test criterion created successfully.", MapToDto(newEntity));
    }

    public async Task<(bool Success, string Message, TestCriterionResponseDto? Data)> UpdateCriterionAsync(
        string id,
        UpdateTestCriterionDto dto,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity == null)
        {
            return (false, "Test criterion not found.", null);
        }

        // Validate clinical range logic
        var validation = ValidateClinicalRanges(dto.NormalMin, dto.NormalMax, dto.PanicLow, dto.PanicHigh);
        if (!validation.IsValid)
        {
            return (false, validation.Message, null);
        }

        entity.CriterionName = dto.CriterionName.Trim();
        entity.EnglishName = dto.EnglishName.Trim();
        entity.Category = dto.Category.Trim();
        entity.Unit = dto.Unit.Trim();
        entity.ValueType = dto.ValueType;
        entity.NormalMin = dto.NormalMin;
        entity.NormalMax = dto.NormalMax;
        entity.NormalText = dto.NormalText?.Trim();
        entity.PanicLow = dto.PanicLow;
        entity.PanicHigh = dto.PanicHigh;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(entity, ct);
        return (true, "Test criterion updated successfully.", MapToDto(entity));
    }

    public async Task<(bool Success, string Message)> DeactivateCriterionAsync(string id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity == null)
        {
            return (false, "Test criterion not found.");
        }

        await _repository.DeactivateAsync(id, ct);
        return (true, "Test criterion has been safely deactivated (historical test results preserved).");
    }

    /// <summary>
    /// Validates biological and panic range inequalities: PanicLow <= NormalMin <= NormalMax <= PanicHigh.
    /// </summary>
    private static (bool IsValid, string Message) ValidateClinicalRanges(
        double? normalMin,
        double? normalMax,
        double? panicLow,
        double? panicHigh)
    {
        if (normalMin.HasValue && normalMax.HasValue && normalMin.Value > normalMax.Value)
        {
            return (false, $"Normal minimum ({normalMin}) cannot exceed normal maximum ({normalMax}).");
        }

        if (panicLow.HasValue && normalMin.HasValue && panicLow.Value > normalMin.Value)
        {
            return (false, $"Panic low critical threshold ({panicLow}) must be less than normal minimum ({normalMin}).");
        }

        if (panicHigh.HasValue && normalMax.HasValue && panicHigh.Value < normalMax.Value)
        {
            return (false, $"Panic high critical threshold ({panicHigh}) must be greater than normal maximum ({normalMax}).");
        }

        return (true, string.Empty);
    }

    private static TestCriterionResponseDto MapToDto(TestCriterionEntity entity)
    {
        string normalRangeStr;
        if (entity.ValueType == "Qualitative")
        {
            normalRangeStr = entity.NormalText ?? "Âm tính (Negative)";
        }
        else if (entity.NormalMin.HasValue && entity.NormalMax.HasValue)
        {
            normalRangeStr = $"{entity.NormalMin.Value:F1} - {entity.NormalMax.Value:F1}";
        }
        else if (entity.NormalMax.HasValue)
        {
            normalRangeStr = $"< {entity.NormalMax.Value:F1}";
        }
        else
        {
            normalRangeStr = "Standard";
        }

        string panicRangeStr = "N/A";
        if (entity.PanicLow.HasValue && entity.PanicHigh.HasValue)
        {
            panicRangeStr = $"< {entity.PanicLow.Value:F1} | > {entity.PanicHigh.Value:F1}";
        }
        else if (entity.PanicHigh.HasValue)
        {
            panicRangeStr = $"> {entity.PanicHigh.Value:F1}";
        }
        else if (entity.PanicLow.HasValue)
        {
            panicRangeStr = $"< {entity.PanicLow.Value:F1}";
        }

        return new TestCriterionResponseDto
        {
            Id = entity.Id,
            CriterionCode = entity.CriterionCode,
            CriterionName = entity.CriterionName,
            EnglishName = entity.EnglishName,
            Category = entity.Category,
            Unit = entity.Unit,
            ValueType = entity.ValueType,
            NormalMin = entity.NormalMin,
            NormalMax = entity.NormalMax,
            NormalText = entity.NormalText,
            FormattedNormalRange = normalRangeStr,
            PanicLow = entity.PanicLow,
            PanicHigh = entity.PanicHigh,
            FormattedPanicRange = panicRangeStr,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt.ToString("dd/MM/yyyy"),
            UpdatedAt = entity.UpdatedAt.ToString("dd/MM/yyyy")
        };
    }
}
