using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// Clinical application service implementing laboratory reference interval configurations and auto-flagging engine (UC57).
/// Fully adheres to: BR-LAB-02 (Biological Reference Intervals), BR-LAB-03 (Panic Values) and ISO 15189:2022.
/// </summary>
public class ReferenceIntervalService : IReferenceIntervalService
{
    private readonly IReferenceIntervalRepository _repository;
    private readonly BMS.Core.Interfaces.ITestCriterionRepository _criterionRepository;

    public ReferenceIntervalService(
        IReferenceIntervalRepository repository,
        BMS.Core.Interfaces.ITestCriterionRepository criterionRepository)
    {
        _repository = repository;
        _criterionRepository = criterionRepository;
    }

    public async Task<PagedResult<ReferenceIntervalResponseDto>> GetIntervalsPagedAsync(
        ReferenceIntervalQueryParameters parameters,
        CancellationToken ct = default)
    {
        var (items, total) = await _repository.GetPagedAsync(
            parameters.Page,
            parameters.PageSize,
            parameters.CriterionCode,
            parameters.Gender,
            parameters.IsActive,
            ct);

        return new PagedResult<ReferenceIntervalResponseDto>
        {
            CurrentPage = parameters.Page,
            PageSize = parameters.PageSize,
            TotalCount = total,
            Items = items.Select(MapToDto).ToList()
        };
    }

    public async Task<List<ReferenceIntervalResponseDto>> GetIntervalsByCriterionCodeAsync(
        string criterionCode,
        bool activeOnly = true,
        CancellationToken ct = default)
    {
        var items = await _repository.GetByCriterionCodeAsync(criterionCode, activeOnly, ct);
        return items.Select(MapToDto).ToList();
    }

    public async Task<ReferenceIntervalResponseDto?> GetIntervalByIdAsync(
        string id,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<(bool Success, string Message, ReferenceIntervalResponseDto? Data)> CreateIntervalAsync(
        CreateReferenceIntervalDto dto,
        CancellationToken ct = default)
    {
        var normalizedCode = dto.CriterionCode.Trim().ToUpperInvariant();

        // 1. Validate age and numerical bounds
        var validation = ValidateIntervalBounds(dto.AgeMin, dto.AgeMax, dto.NormalMin, dto.NormalMax, dto.PanicLow, dto.PanicHigh);
        if (!validation.IsValid)
        {
            return (false, validation.Message, null);
        }

        // 2. Check criterion existence and enrich metadata
        var criterion = await _criterionRepository.GetByCodeAsync(normalizedCode, ct);
        var criterionId = dto.CriterionId ?? criterion?.Id ?? string.Empty;
        var criterionName = !string.IsNullOrWhiteSpace(dto.CriterionName) 
            ? dto.CriterionName.Trim() 
            : (criterion?.CriterionName ?? normalizedCode);
        var unit = !string.IsNullOrWhiteSpace(dto.Unit)
            ? dto.Unit.Trim()
            : (criterion?.Unit ?? string.Empty);

        // 3. Generate demographic label if missing
        var demographicLabel = !string.IsNullOrWhiteSpace(dto.DemographicLabel)
            ? dto.DemographicLabel.Trim()
            : GenerateDemographicLabel(dto.Gender, dto.AgeMin, dto.AgeMax, dto.AgeUnit, dto.Condition);

        var entity = new ReferenceIntervalEntity
        {
            CriterionId = criterionId,
            CriterionCode = normalizedCode,
            CriterionName = criterionName,
            Unit = unit,
            Gender = NormalizeGender(dto.Gender),
            AgeMin = dto.AgeMin,
            AgeMax = dto.AgeMax,
            AgeUnit = string.IsNullOrWhiteSpace(dto.AgeUnit) ? "Years" : dto.AgeUnit.Trim(),
            DemographicLabel = demographicLabel,
            NormalMin = dto.NormalMin,
            NormalMax = dto.NormalMax,
            NormalText = dto.NormalText?.Trim(),
            PanicLow = dto.PanicLow,
            PanicHigh = dto.PanicHigh,
            Condition = string.IsNullOrWhiteSpace(dto.Condition) ? "General" : dto.Condition.Trim(),
            StandardAuthority = string.IsNullOrWhiteSpace(dto.StandardAuthority) ? "Quyết định 2429/QĐ-BYT" : dto.StandardAuthority.Trim(),
            Notes = dto.Notes?.Trim() ?? string.Empty,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _repository.CreateAsync(entity, ct);
        return (true, "Khoảng tham chiếu sinh học đã được cấu hình thành công.", MapToDto(created));
    }

    public async Task<(bool Success, string Message, ReferenceIntervalResponseDto? Data)> UpdateIntervalAsync(
        string id,
        UpdateReferenceIntervalDto dto,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing == null)
        {
            return (false, "Không tìm thấy khoảng tham chiếu cần cập nhật.", null);
        }

        var validation = ValidateIntervalBounds(dto.AgeMin, dto.AgeMax, dto.NormalMin, dto.NormalMax, dto.PanicLow, dto.PanicHigh);
        if (!validation.IsValid)
        {
            return (false, validation.Message, null);
        }

        if (!string.IsNullOrWhiteSpace(dto.CriterionName))
            existing.CriterionName = dto.CriterionName.Trim();

        if (!string.IsNullOrWhiteSpace(dto.Unit))
            existing.Unit = dto.Unit.Trim();

        existing.Gender = NormalizeGender(dto.Gender);
        existing.AgeMin = dto.AgeMin;
        existing.AgeMax = dto.AgeMax;
        existing.AgeUnit = string.IsNullOrWhiteSpace(dto.AgeUnit) ? "Years" : dto.AgeUnit.Trim();

        existing.DemographicLabel = !string.IsNullOrWhiteSpace(dto.DemographicLabel)
            ? dto.DemographicLabel.Trim()
            : GenerateDemographicLabel(existing.Gender, existing.AgeMin, existing.AgeMax, existing.AgeUnit, dto.Condition);

        existing.NormalMin = dto.NormalMin;
        existing.NormalMax = dto.NormalMax;
        existing.NormalText = dto.NormalText?.Trim();
        existing.PanicLow = dto.PanicLow;
        existing.PanicHigh = dto.PanicHigh;

        if (!string.IsNullOrWhiteSpace(dto.Condition))
            existing.Condition = dto.Condition.Trim();

        if (!string.IsNullOrWhiteSpace(dto.StandardAuthority))
            existing.StandardAuthority = dto.StandardAuthority.Trim();

        existing.Notes = dto.Notes?.Trim() ?? string.Empty;
        existing.IsActive = dto.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        var updated = await _repository.UpdateAsync(existing, ct);
        return (true, "Cập nhật khoảng tham chiếu thành công.", MapToDto(updated!));
    }

    public async Task<(bool Success, string Message)> DeactivateIntervalAsync(
        string id,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing == null)
        {
            return (false, "Không tìm thấy khoảng tham chiếu.");
        }

        var deactivated = await _repository.DeactivateAsync(id, ct);
        return deactivated
            ? (true, "Khoảng tham chiếu đã được khóa an toàn (dữ liệu kết quả lịch sử được bảo toàn).")
            : (false, "Không thể khóa khoảng tham chiếu.");
    }

    public async Task<EvaluateTestResultResponseDto> EvaluateResultAsync(
        EvaluateTestResultRequestDto request,
        CancellationToken ct = default)
    {
        var normalizedCode = request.CriterionCode.Trim().ToUpperInvariant();
        var intervals = await _repository.GetByCriterionCodeAsync(normalizedCode, activeOnly: true, ct);

        // Normalize patient gender
        var patientGender = NormalizeGender(request.PatientGender);

        // Find best matching interval based on:
        // 1. Exact Gender match & Age range
        // 2. Gender "All" & Age range
        var matched = intervals.FirstOrDefault(i =>
            (i.Gender.Equals(patientGender, StringComparison.OrdinalIgnoreCase) || i.Gender == "All") &&
            request.PatientAge >= i.AgeMin && request.PatientAge <= i.AgeMax &&
            (string.IsNullOrWhiteSpace(request.Condition) || i.Condition.Equals(request.Condition, StringComparison.OrdinalIgnoreCase) || i.Condition == "General"))
            ?? intervals.FirstOrDefault(i => request.PatientAge >= i.AgeMin && request.PatientAge <= i.AgeMax)
            ?? intervals.FirstOrDefault();

        var response = new EvaluateTestResultResponseDto
        {
            CriterionCode = normalizedCode,
            ValueEvaluated = request.NumericValue.HasValue 
                ? request.NumericValue.Value.ToString("0.##") 
                : (request.TextValue ?? "N/A"),
            MatchedInterval = matched != null ? MapToDto(matched) : null
        };

        if (matched == null)
        {
            response.Flag = "NORMAL";
            response.FlagColor = "success";
            response.AlertMessage = "Không có khoảng tham chiếu đặc hiệu, chỉ số chưa được phân loại.";
            return response;
        }

        // Quantitative numeric evaluation
        if (request.NumericValue.HasValue)
        {
            var val = request.NumericValue.Value;

            // 1. Panic Low Check (BR-LAB-03)
            if (matched.PanicLow.HasValue && val <= matched.PanicLow.Value)
            {
                response.Flag = "PANIC_LOW";
                response.FlagColor = "danger";
                response.IsPanic = true;
                response.MessageCode = "MSG-WRN-02";
                response.AlertMessage = $"NGUY HIỂM: Chỉ số {matched.CriterionName} ({val} {matched.Unit}) chạm ngưỡng PANIC thấp đe dọa sinh mạng! (< {matched.PanicLow.Value} {matched.Unit})";
                return response;
            }

            // 2. Panic High Check (BR-LAB-03)
            if (matched.PanicHigh.HasValue && val >= matched.PanicHigh.Value)
            {
                response.Flag = "PANIC_HIGH";
                response.FlagColor = "danger";
                response.IsPanic = true;
                response.MessageCode = "MSG-WRN-02";
                response.AlertMessage = $"NGUY HIỂM: Chỉ số {matched.CriterionName} ({val} {matched.Unit}) chạm ngưỡng PANIC cao đe dọa sinh mạng! (> {matched.PanicHigh.Value} {matched.Unit})";
                return response;
            }

            // 3. Normal Range Low Check (BR-LAB-02)
            if (matched.NormalMin.HasValue && val < matched.NormalMin.Value)
            {
                response.Flag = "L";
                response.FlagColor = "warning";
                response.IsPanic = false;
                response.MessageCode = "MSG-WRN-01";
                response.AlertMessage = $"Cảnh báo: Chỉ số {matched.CriterionName} ({val} {matched.Unit}) thấp hơn khoảng tham chiếu an toàn (< {matched.NormalMin.Value} {matched.Unit}).";
                return response;
            }

            // 4. Normal Range High Check (BR-LAB-02)
            if (matched.NormalMax.HasValue && val > matched.NormalMax.Value)
            {
                response.Flag = "H";
                response.FlagColor = "danger";
                response.IsPanic = false;
                response.MessageCode = "MSG-WRN-01";
                response.AlertMessage = $"Cảnh báo: Chỉ số {matched.CriterionName} ({val} {matched.Unit}) cao hơn khoảng tham chiếu an toàn (> {matched.NormalMax.Value} {matched.Unit}).";
                return response;
            }

            // 5. Normal In-Range
            response.Flag = "NORMAL";
            response.FlagColor = "success";
            response.IsPanic = false;
            response.AlertMessage = $"Chỉ số {matched.CriterionName} ({val} {matched.Unit}) nằm trong giới hạn sinh học an toàn.";
            return response;
        }

        // Qualitative textual evaluation
        if (!string.IsNullOrWhiteSpace(request.TextValue) && !string.IsNullOrWhiteSpace(matched.NormalText))
        {
            var isNegativeNormal = request.TextValue.Trim().Equals(matched.NormalText.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                   request.TextValue.Trim().Contains("âm tính", StringComparison.OrdinalIgnoreCase) ||
                                   request.TextValue.Trim().Contains("negative", StringComparison.OrdinalIgnoreCase);

            if (isNegativeNormal)
            {
                response.Flag = "NORMAL";
                response.FlagColor = "success";
                response.AlertMessage = $"Kết quả định tính bình thường ({request.TextValue}).";
            }
            else
            {
                response.Flag = "H";
                response.FlagColor = "danger";
                response.MessageCode = "MSG-WRN-01";
                response.AlertMessage = $"Cảnh báo: Kết quả định tính dương tính / bất thường ({request.TextValue}).";
            }
            return response;
        }

        response.Flag = "NORMAL";
        response.FlagColor = "success";
        response.AlertMessage = "Kết quả hợp lệ.";
        return response;
    }

    private static (bool IsValid, string Message) ValidateIntervalBounds(
        double ageMin,
        double ageMax,
        double? normalMin,
        double? normalMax,
        double? panicLow,
        double? panicHigh)
    {
        if (ageMin > ageMax)
        {
            return (false, $"Tuổi tối thiểu ({ageMin}) không được lớn hơn tuổi tối đa ({ageMax}).");
        }

        if (normalMin.HasValue && normalMax.HasValue && normalMin.Value > normalMax.Value)
        {
            return (false, $"Giới hạn dưới tham chiếu ({normalMin}) không được lớn hơn giới hạn trên ({normalMax}).");
        }

        if (panicLow.HasValue && normalMin.HasValue && panicLow.Value > normalMin.Value)
        {
            return (false, $"Ngưỡng Panic nguy kịch dưới ({panicLow}) phải nhỏ hơn hoặc bằng giới hạn dưới bình thường ({normalMin}).");
        }

        if (panicHigh.HasValue && normalMax.HasValue && panicHigh.Value < normalMax.Value)
        {
            return (false, $"Ngưỡng Panic nguy kịch trên ({panicHigh}) phải lớn hơn hoặc bằng giới hạn trên bình thường ({normalMax}).");
        }

        return (true, string.Empty);
    }

    private static string NormalizeGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return "All";
        var g = gender.Trim().ToUpperInvariant();
        if (g == "MALE" || g == "NAM" || g == "M") return "Male";
        if (g == "FEMALE" || g == "NU" || g == "NỮ" || g == "F") return "Female";
        return "All";
    }

    private static string GenerateDemographicLabel(string gender, double ageMin, double ageMax, string ageUnit, string? condition)
    {
        var genderStr = gender switch
        {
            "Male" => "Nam",
            "Female" => "Nữ",
            _ => "Tất cả giới tính"
        };

        var unitStr = ageUnit.Equals("Days", StringComparison.OrdinalIgnoreCase) ? "ngày"
                    : ageUnit.Equals("Months", StringComparison.OrdinalIgnoreCase) ? "tháng"
                    : "tuổi";

        var ageStr = (ageMin == 0 && ageMax >= 120) ? "Mọi độ tuổi" : $"{ageMin} - {ageMax} {unitStr}";
        var condStr = !string.IsNullOrWhiteSpace(condition) && condition != "General" ? $" ({condition})" : "";

        return $"{genderStr} [{ageStr}]{condStr}";
    }

    private static ReferenceIntervalResponseDto MapToDto(ReferenceIntervalEntity entity)
    {
        string normalRangeStr;
        if (!string.IsNullOrWhiteSpace(entity.NormalText))
        {
            normalRangeStr = entity.NormalText;
        }
        else if (entity.NormalMin.HasValue && entity.NormalMax.HasValue)
        {
            normalRangeStr = $"{entity.NormalMin.Value:0.##} - {entity.NormalMax.Value:0.##} {entity.Unit}".Trim();
        }
        else if (entity.NormalMax.HasValue)
        {
            normalRangeStr = $"< {entity.NormalMax.Value:0.##} {entity.Unit}".Trim();
        }
        else if (entity.NormalMin.HasValue)
        {
            normalRangeStr = $"> {entity.NormalMin.Value:0.##} {entity.Unit}".Trim();
        }
        else
        {
            normalRangeStr = "Chuẩn";
        }

        string panicRangeStr = "Không áp dụng";
        if (entity.PanicLow.HasValue && entity.PanicHigh.HasValue)
        {
            panicRangeStr = $"< {entity.PanicLow.Value:0.##} | > {entity.PanicHigh.Value:0.##} {entity.Unit}".Trim();
        }
        else if (entity.PanicHigh.HasValue)
        {
            panicRangeStr = $"> {entity.PanicHigh.Value:0.##} {entity.Unit}".Trim();
        }
        else if (entity.PanicLow.HasValue)
        {
            panicRangeStr = $"< {entity.PanicLow.Value:0.##} {entity.Unit}".Trim();
        }

        return new ReferenceIntervalResponseDto
        {
            Id = entity.Id,
            CriterionId = entity.CriterionId,
            CriterionCode = entity.CriterionCode,
            CriterionName = entity.CriterionName,
            Unit = entity.Unit,
            Gender = entity.Gender,
            AgeMin = entity.AgeMin,
            AgeMax = entity.AgeMax,
            AgeUnit = entity.AgeUnit,
            DemographicLabel = entity.DemographicLabel,
            NormalMin = entity.NormalMin,
            NormalMax = entity.NormalMax,
            NormalText = entity.NormalText,
            FormattedNormalRange = normalRangeStr,
            PanicLow = entity.PanicLow,
            PanicHigh = entity.PanicHigh,
            FormattedPanicRange = panicRangeStr,
            Condition = entity.Condition,
            StandardAuthority = entity.StandardAuthority,
            Notes = entity.Notes,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
            UpdatedAt = entity.UpdatedAt.ToString("dd/MM/yyyy HH:mm")
        };
    }
}
