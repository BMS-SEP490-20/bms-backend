using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Clinical application service implementing laboratory reference interval configurations and auto-flagging engine (UC57).
/// Fully adheres to: BR-LAB-02 (Biological Reference Intervals), BR-LAB-03 (Panic Values), ISO 15189:2022 and ISO 27789:2021.
/// </summary>
public class ReferenceIntervalService : IReferenceIntervalService
{
    private readonly IReferenceIntervalRepository _repository;
    private readonly BMS.Core.Interfaces.ITestCriterionRepository _criterionRepository;
    private readonly IMongoDatabase? _db;

    public ReferenceIntervalService(
        IReferenceIntervalRepository repository,
        BMS.Core.Interfaces.ITestCriterionRepository criterionRepository,
        IMongoDatabase? db = null)
    {
        _repository = repository;
        _criterionRepository = criterionRepository;
        _db = db;
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
        string? currentUserId = null,
        string? currentUserName = null,
        CancellationToken ct = default)
    {
        var testId = !string.IsNullOrWhiteSpace(dto.TestId) 
            ? dto.TestId.Trim() 
            : (!string.IsNullOrWhiteSpace(dto.CriterionCode) ? dto.CriterionCode.Trim() : dto.CriterionId?.Trim() ?? string.Empty);

        if (string.IsNullOrWhiteSpace(testId))
        {
            return (false, "Mã xét nghiệm (TestId / CriterionCode) là bắt buộc.", null);
        }

        var normalizedCode = testId.ToUpperInvariant();
        var lowerLimit = dto.LowerLimit ?? dto.NormalMin;
        var upperLimit = dto.UpperLimit ?? dto.NormalMax;

        // 1. Validate age and numerical bounds (LowerLimit <= UpperLimit)
        var validation = ValidateIntervalBounds(dto.AgeMin, dto.AgeMax, lowerLimit, upperLimit, dto.PanicLow, dto.PanicHigh);
        if (!validation.IsValid)
        {
            return (false, validation.Message, null);
        }

        // 2. Check criterion existence and verify unit compatibility (ISO 15189 / Business Logic)
        var criterion = await _criterionRepository.GetByCodeAsync(normalizedCode, ct);
        if (criterion == null && normalizedCode.StartsWith("TEST-"))
        {
            criterion = await _criterionRepository.GetByCodeAsync(normalizedCode.Replace("TEST-", ""), ct);
        }

        if (criterion != null && !string.IsNullOrWhiteSpace(criterion.Unit) && !string.IsNullOrWhiteSpace(dto.Unit))
        {
            if (!IsUnitCompatible(criterion.Unit, dto.Unit))
            {
                return (false, $"Đơn vị đo '{dto.Unit}' không tương thích với xét nghiệm '{normalizedCode}' (Đơn vị xét nghiệm: '{criterion.Unit}').", null);
            }
        }

        var criterionId = dto.CriterionId ?? criterion?.Id ?? normalizedCode;
        var criterionName = !string.IsNullOrWhiteSpace(dto.CriterionName) 
            ? dto.CriterionName.Trim() 
            : (criterion?.CriterionName ?? normalizedCode);
        var unit = !string.IsNullOrWhiteSpace(dto.Unit)
            ? dto.Unit.Trim()
            : (criterion?.Unit ?? string.Empty);

        // 2b. Chặn tạo trùng lặp: Mỗi loại Chỉ Số Xét Nghiệm chỉ có tối đa 2 khoảng tham chiếu (1 Nam, 1 Nữ)
        var targetGender = NormalizeGender(dto.Gender);
        if (targetGender != "Male" && targetGender != "Female")
        {
            return (false, "Giới tính áp dụng cho khoảng tham chiếu phải là 'Nam' (Male) hoặc 'Nữ' (Female).", null);
        }

        var existingIntervals = await _repository.GetByCriterionCodeAsync(normalizedCode, activeOnly: false, ct);
        var activeIntervals = existingIntervals.Where(x => !x.IsDeleted && x.IsActive).ToList();

        // 1. Kiểm tra xem đã có khoảng tham chiếu cho giới tính này chưa
        var duplicateGender = activeIntervals.FirstOrDefault(x => 
            string.Equals(x.Gender, targetGender, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Gender, "All", StringComparison.OrdinalIgnoreCase));

        if (duplicateGender != null)
        {
            var genderVi = targetGender == "Male" ? "Nam" : "Nữ";
            return (false, $"Chỉ số xét nghiệm '{normalizedCode}' ({criterionName}) đã có khoảng tham chiếu cho giới tính {genderVi} (Mã: {duplicateGender.Id}). Mỗi loại chỉ số xét nghiệm chỉ được cấu hình tối đa 2 khoảng tham chiếu: 1 cho Nam và 1 cho Nữ.", null);
        }

        // 2. Kiểm tra tổng số lượng: Tối đa 2 khoảng (1 Nam, 1 Nữ)
        if (activeIntervals.Count >= 2)
        {
            return (false, $"Chỉ số xét nghiệm '{normalizedCode}' ({criterionName}) đã có đủ 2 khoảng tham chiếu (1 Nam, 1 Nữ). Không thể tạo thêm khoảng tham chiếu mới cho chỉ số này (vui lòng chỉnh sửa khoảng tham chiếu hiện có thay vì tạo mới).", null);
        }

        if (!string.IsNullOrWhiteSpace(criterionId) && !criterionId.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase))
        {
            var byId = await _repository.GetByCriterionIdAsync(criterionId, activeOnly: false, ct);
            var activeById = byId.Where(x => !x.IsDeleted && x.IsActive).ToList();
            var duplicateById = activeById.FirstOrDefault(x =>
                string.Equals(x.Gender, targetGender, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Gender, "All", StringComparison.OrdinalIgnoreCase));
            if (duplicateById != null)
            {
                var genderVi = targetGender == "Male" ? "Nam" : "Nữ";
                return (false, $"Chỉ số xét nghiệm '{normalizedCode}' ({criterionName}) đã có khoảng tham chiếu cho giới tính {genderVi} (Mã: {duplicateById.Id}). Mỗi loại chỉ số xét nghiệm chỉ được cấu hình tối đa 2 khoảng tham chiếu: 1 cho Nam và 1 cho Nữ.", null);
            }
            if (activeById.Count >= 2)
            {
                return (false, $"Chỉ số xét nghiệm '{normalizedCode}' ({criterionName}) đã có đủ 2 khoảng tham chiếu (1 Nam, 1 Nữ). Không thể tạo thêm khoảng tham chiếu mới cho chỉ số này.", null);
            }
        }

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
            NormalMin = lowerLimit,
            NormalMax = upperLimit,
            NormalText = dto.NormalText?.Trim(),
            PanicLow = dto.PanicLow,
            PanicHigh = dto.PanicHigh,
            Condition = string.IsNullOrWhiteSpace(dto.Condition) ? "General" : dto.Condition.Trim(),
            StandardAuthority = string.IsNullOrWhiteSpace(dto.StandardAuthority) ? "Quyết định 2429/QĐ-BYT" : dto.StandardAuthority.Trim(),
            Notes = dto.Notes?.Trim() ?? string.Empty,
            EffectiveFrom = dto.EffectiveFrom ?? DateTime.UtcNow,
            EffectiveTo = dto.EffectiveTo,
            Status = !string.IsNullOrWhiteSpace(dto.Status) ? dto.Status.Trim().ToUpperInvariant() : "ACTIVE",
            CreatedBy = currentUserId ?? dto.CreatedBy ?? "System",
            ApprovedBy = dto.ApprovedBy?.Trim(),
            Version = 1,
            PreviousVersions = new List<ReferenceIntervalVersionSnapshot>(),
            IsActive = dto.IsActive && (!string.Equals(dto.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase)),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _repository.CreateAsync(entity, ct);

        // ISO 27789 Audit Trail for creation
        if (_db != null)
        {
            try
            {
                var auditCol = _db.GetCollection<AuditLog>("audit_logs");
                await auditCol.InsertOneAsync(new AuditLog
                {
                    AuditId = $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = DateTime.UtcNow,
                    UserId = currentUserId ?? dto.CreatedBy ?? "System",
                    UserName = currentUserName ?? currentUserId ?? "LabStaff",
                    UserRole = RoleNames.Manager,
                    Action = "CREATE_REFERENCE_INTERVAL",
                    Category = "LAB_CONFIGURATION",
                    Severity = "INFO",
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "ReferenceInterval",
                    EntityId = created.Id,
                    Details = $"Cấu hình mới khoảng tham chiếu cho xét nghiệm {created.CriterionCode} ({created.Gender}, {created.AgeMin}-{created.AgeMax} {created.AgeUnit}): {created.NormalMin} - {created.NormalMax} {created.Unit}."
                }, cancellationToken: ct);
            }
            catch { }
        }

        return (true, "Khoảng tham chiếu sinh học đã được cấu hình thành công.", MapToDto(created));
    }

    public async Task<(bool Success, string Message, ReferenceIntervalResponseDto? Data)> UpdateIntervalAsync(
        string id,
        UpdateReferenceIntervalDto dto,
        string? currentUserId = null,
        string? currentUserName = null,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing == null)
        {
            return (false, "Không tìm thấy khoảng tham chiếu cần cập nhật.", null);
        }

        var lowerLimit = dto.LowerLimit ?? dto.NormalMin;
        var upperLimit = dto.UpperLimit ?? dto.NormalMax;

        var validation = ValidateIntervalBounds(dto.AgeMin, dto.AgeMax, lowerLimit, upperLimit, dto.PanicLow, dto.PanicHigh);
        if (!validation.IsValid)
        {
            return (false, validation.Message, null);
        }

        // Unit compatibility check
        if (!string.IsNullOrWhiteSpace(dto.Unit) && !string.IsNullOrWhiteSpace(existing.Unit))
        {
            if (!IsUnitCompatible(existing.Unit, dto.Unit))
            {
                return (false, $"Đơn vị đo '{dto.Unit}' không tương thích với xét nghiệm '{existing.CriterionCode}' (Đơn vị xét nghiệm: '{existing.Unit}').", null);
            }
        }

        // Chặn trùng lặp giới tính khi cập nhật: Mỗi chỉ số xét nghiệm chỉ có 1 Nam và 1 Nữ
        var newGender = NormalizeGender(dto.Gender);
        if (newGender != "Male" && newGender != "Female")
        {
            return (false, "Giới tính áp dụng cho khoảng tham chiếu phải là 'Nam' (Male) hoặc 'Nữ' (Female).", null);
        }

        if (!string.Equals(existing.Gender, newGender, StringComparison.OrdinalIgnoreCase))
        {
            var existingIntervals = await _repository.GetByCriterionCodeAsync(existing.CriterionCode, activeOnly: false, ct);
            var duplicateGender = existingIntervals.FirstOrDefault(x =>
                !x.IsDeleted &&
                x.IsActive &&
                x.Id != id &&
                (string.Equals(x.Gender, newGender, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Gender, "All", StringComparison.OrdinalIgnoreCase)));

            if (duplicateGender != null)
            {
                var genderVi = newGender == "Male" ? "Nam" : "Nữ";
                return (false, $"Không thể đổi giới tính sang {genderVi} vì chỉ số '{existing.CriterionCode}' đã có khoảng tham chiếu cho giới tính {genderVi} (Mã: {duplicateGender.Id}). Mỗi loại chỉ số chỉ được cấu hình tối đa 1 khoảng cho Nam và 1 khoảng cho Nữ.", null);
            }
        }

        // Archive prior version into PreviousVersions (Version Control / Audit Trail)
        existing.PreviousVersions.Add(new ReferenceIntervalVersionSnapshot
        {
            Version = existing.Version,
            LowerLimit = existing.NormalMin,
            UpperLimit = existing.NormalMax,
            Unit = existing.Unit,
            Gender = existing.Gender,
            AgeMin = existing.AgeMin,
            AgeMax = existing.AgeMax,
            EffectiveFrom = existing.EffectiveFrom,
            EffectiveTo = DateTime.UtcNow,
            Status = existing.Status,
            ChangedBy = currentUserId ?? "System",
            ChangedAt = DateTime.UtcNow,
            ChangeReason = dto.ChangeReason ?? "Cập nhật khoảng tham chiếu"
        });

        existing.Version++;
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

        existing.NormalMin = lowerLimit;
        existing.NormalMax = upperLimit;
        existing.NormalText = dto.NormalText?.Trim();
        existing.PanicLow = dto.PanicLow;
        existing.PanicHigh = dto.PanicHigh;

        if (!string.IsNullOrWhiteSpace(dto.Condition))
            existing.Condition = dto.Condition.Trim();

        if (!string.IsNullOrWhiteSpace(dto.StandardAuthority))
            existing.StandardAuthority = dto.StandardAuthority.Trim();

        if (!string.IsNullOrWhiteSpace(dto.Notes))
            existing.Notes = dto.Notes.Trim();

        if (dto.EffectiveFrom.HasValue)
            existing.EffectiveFrom = dto.EffectiveFrom.Value;

        if (dto.EffectiveTo.HasValue)
            existing.EffectiveTo = dto.EffectiveTo.Value;

        if (!string.IsNullOrWhiteSpace(dto.Status))
        {
            existing.Status = dto.Status.Trim().ToUpperInvariant();
            existing.IsActive = !string.Equals(existing.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase) && dto.IsActive;
        }
        else
        {
            existing.IsActive = dto.IsActive;
        }

        if (!string.IsNullOrWhiteSpace(dto.ApprovedBy))
            existing.ApprovedBy = dto.ApprovedBy.Trim();

        existing.UpdatedAt = DateTime.UtcNow;

        var updated = await _repository.UpdateAsync(existing, ct);

        // ISO 27789 Audit Trail for modification
        if (_db != null)
        {
            try
            {
                var auditCol = _db.GetCollection<AuditLog>("audit_logs");
                await auditCol.InsertOneAsync(new AuditLog
                {
                    AuditId = $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = DateTime.UtcNow,
                    UserId = currentUserId ?? "System",
                    UserName = currentUserName ?? currentUserId ?? "LabStaff",
                    UserRole = RoleNames.Manager,
                    Action = "UPDATE_REFERENCE_INTERVAL",
                    Category = "LAB_CONFIGURATION",
                    Severity = "INFO",
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "ReferenceInterval",
                    EntityId = existing.Id,
                    Details = $"Cập nhật khoảng tham chiếu {existing.CriterionCode} sang Phiên bản {existing.Version} ({existing.Gender}, {existing.AgeMin}-{existing.AgeMax} {existing.AgeUnit}): {existing.NormalMin} - {existing.NormalMax} {existing.Unit}."
                }, cancellationToken: ct);
            }
            catch { }
        }

        return (true, "Cập nhật khoảng tham chiếu thành công.", MapToDto(updated!));
    }

    public async Task<(bool Success, string Message)> DeactivateIntervalAsync(
        string id,
        string? currentUserId = null,
        string? currentUserName = null,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing == null)
        {
            return (false, "Không tìm thấy khoảng tham chiếu.");
        }

        var deactivated = await _repository.DeactivateAsync(id, ct);
        if (deactivated && _db != null)
        {
            try
            {
                var auditCol = _db.GetCollection<AuditLog>("audit_logs");
                await auditCol.InsertOneAsync(new AuditLog
                {
                    AuditId = $"AUD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = DateTime.UtcNow,
                    UserId = currentUserId ?? "System",
                    UserName = currentUserName ?? currentUserId ?? "LabStaff",
                    UserRole = RoleNames.Manager,
                    Action = "DEACTIVATE_REFERENCE_INTERVAL",
                    Category = "LAB_CONFIGURATION",
                    Severity = "WARNING",
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "ReferenceInterval",
                    EntityId = id,
                    Details = $"Khóa khoảng tham chiếu {existing.CriterionCode} ({existing.DemographicLabel}). Dữ liệu lịch sử xét nghiệm cũ được bảo toàn."
                }, cancellationToken: ct);
            }
            catch { }
        }

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

    private static bool IsUnitCompatible(string testUnit, string inputUnit)
    {
        if (string.IsNullOrWhiteSpace(testUnit) || string.IsNullOrWhiteSpace(inputUnit)) return true;
        var u1 = testUnit.Trim().ToLowerInvariant();
        var u2 = inputUnit.Trim().ToLowerInvariant();
        if (u1 == u2) return true;

        // Known compatible laboratory unit groups (e.g. concentration equivalents)
        string[][] compatibleGroups = new[]
        {
            new[] { "mmol/l", "mg/dl", "mg/l", "g/l" },
            new[] { "g/dl", "g/l", "mg/dl" },
            new[] { "u/l", "iu/l", "ukat/l" },
            new[] { "10^9/l", "g/l", "k/ul", "/ul", "cells/ul" },
            new[] { "10^12/l", "t/l", "m/ul" },
            new[] { "%", "percent", "tỉ lệ" },
            new[] { "sec", "s", "giây" }
        };

        foreach (var group in compatibleGroups)
        {
            if (group.Contains(u1) && group.Contains(u2)) return true;
        }

        return false;
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
            TestId = entity.TestId,
            CriterionId = entity.CriterionId,
            CriterionCode = entity.CriterionCode,
            CriterionName = entity.CriterionName,
            Unit = entity.Unit,
            Gender = entity.Gender,
            AgeMin = entity.AgeMin,
            AgeMax = entity.AgeMax,
            AgeUnit = entity.AgeUnit,
            DemographicLabel = entity.DemographicLabel,
            LowerLimit = entity.NormalMin,
            UpperLimit = entity.NormalMax,
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
            EffectiveFrom = entity.EffectiveFrom,
            EffectiveTo = entity.EffectiveTo,
            Status = entity.Status,
            CreatedBy = entity.CreatedBy,
            ApprovedBy = entity.ApprovedBy,
            Version = entity.Version,
            PreviousVersions = entity.PreviousVersions ?? new List<ReferenceIntervalVersionSnapshot>(),
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
            UpdatedAt = entity.UpdatedAt.ToString("dd/MM/yyyy HH:mm")
        };
    }
}
