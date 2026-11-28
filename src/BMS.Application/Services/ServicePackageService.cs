using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.ServicePackages;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <summary>
/// Business service implementing laboratory test package management workflows (UC15).
/// Enforces business rules:
/// - BR-PKG-01: Unique uppercase package code.
/// - BR-PKG-02: Non-negative price validation.
/// - BR-PKG-03: Soft delete to maintain auditability.
/// - OWASP ASVS: Access Control, Data Protection, Audit Trail (ISO 27789).
/// </summary>
public sealed class ServicePackageService : IServicePackageService
{
    private readonly IServicePackageRepository _packages;
    private readonly TimeProvider _clock;
    private readonly ILogger<ServicePackageService> _logger;
    private readonly IAuditLogRepository? _auditLogs;
    private readonly BMS.Core.Interfaces.ITestCriterionRepository? _testCriteria;

    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
        { "name", "code", "price", "createdAt" };

    public ServicePackageService(
        IServicePackageRepository packages,
        TimeProvider clock,
        ILogger<ServicePackageService> logger,
        IAuditLogRepository? auditLogs = null,
        BMS.Core.Interfaces.ITestCriterionRepository? testCriteria = null)
    {
        _packages = packages;
        _clock = clock;
        _logger = logger;
        _auditLogs = auditLogs;
        _testCriteria = testCriteria;
    }

    public Task<PagedResult<ServicePackageDto>> ListAsync(
        ServicePackageListRequest request, bool includeInactive, CancellationToken ct = default)
        => QueryAsync(request, keyword: null, minPrice: null, maxPrice: null, includeInactive, ct);

    public Task<PagedResult<ServicePackageDto>> SearchAsync(
        ServicePackageSearchRequest request, bool includeInactive, CancellationToken ct = default)
        => QueryAsync(request, request.Keyword, request.MinPrice, request.MaxPrice, includeInactive, ct);

    public async Task<ServicePackageDto> GetByIdAsync(string id, bool includeInactive, CancellationToken ct = default)
    {
        var p = await _packages.GetByIdAsync(id, ct);

        if (p is null || (!includeInactive && !p.IsActive))
            throw new ServicePackageNotFoundException();
        return ToDto(p, canManage: includeInactive);
    }

    public async Task<ServicePackageDto> CreateAsync(CreateServicePackageRequest request, string userId, CancellationToken ct = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _packages.CodeExistsAsync(code, ct))
            throw new ServicePackageCodeAlreadyExistsException(code);

        await ValidateTestsAsync(request.Tests, ct);

        var now = _clock.GetUtcNow().UtcDateTime;
        var entity = new ServicePackage
        {
            Code = code,
            CreatedAt = now,
            CreatedBy = userId
        };
        Apply(entity, request);

        await _packages.InsertAsync(entity, ct);
        _logger.LogInformation("Service package created: {Id} ({Code}) by {UserId}", entity.Id, entity.Code, userId);

        if (_auditLogs != null)
        {
            try
            {
                await _auditLogs.AddAsync(new AuditLog
                {
                    AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = now,
                    UserId = userId,
                    UserName = userId,
                    Action = "SERVICE_PACKAGE_CREATED",
                    Category = "SERVICE_PACKAGE",
                    Severity = "INFO",
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "ServicePackage",
                    EntityId = entity.Id,
                    Details = $"Created service package {entity.Code} ('{entity.Name}') with price {entity.Price:N0} VND."
                }, ct);
            }
            catch {  }
        }

        return ToDto(entity, canManage: true);
    }

    public async Task<ServicePackageDto> UpdateAsync(string id, UpdateServicePackageRequest request, string userId, CancellationToken ct = default)
    {
        var entity = await _packages.GetByIdAsync(id, ct) ?? throw new ServicePackageNotFoundException();

        await ValidateTestsAsync(request.Tests, ct);

        var now = _clock.GetUtcNow().UtcDateTime;
        var oldPrice = entity.Price;
        var oldName = entity.Name;
        var oldIsActive = entity.IsActive;

        Apply(entity, request);
        entity.UpdatedAt = now;
        entity.UpdatedBy = userId;

        if (!await _packages.ReplaceAsync(entity, ct))
            throw new ServicePackageNotFoundException();

        _logger.LogInformation("Service package updated: {Id} by {UserId}", entity.Id, userId);

        if (_auditLogs != null)
        {
            try
            {
                var changes = new List<string>();
                if (oldPrice != entity.Price)
                    changes.Add($"Price changed from {oldPrice:N0} to {entity.Price:N0}");
                if (oldName != entity.Name)
                    changes.Add($"Name changed from '{oldName}' to '{entity.Name}'");
                if (oldIsActive != entity.IsActive)
                    changes.Add($"IsActive changed from {oldIsActive} to {entity.IsActive}");

                await _auditLogs.AddAsync(new AuditLog
                {
                    AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = now,
                    UserId = userId,
                    UserName = userId,
                    Action = "SERVICE_PACKAGE_UPDATED",
                    Category = "SERVICE_PACKAGE",
                    Severity = "INFO",
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "ServicePackage",
                    EntityId = entity.Id,
                    Details = changes.Count > 0
                        ? $"Updated service package {entity.Code}: {string.Join("; ", changes)}."
                        : $"Updated service package {entity.Code}."
                }, ct);
            }
            catch {  }
        }

        return ToDto(entity, canManage: true);
    }

    public async Task DeleteAsync(string id, string userId, CancellationToken ct = default)
    {
        var existing = await _packages.GetByIdAsync(id, ct) ?? throw new ServicePackageNotFoundException();

        if (await _packages.HasActiveDependenciesAsync(existing.Id, existing.Code, ct))
            throw new ServicePackageInUseException(existing.Code, "Gói dịch vụ đang được sử dụng trong lịch hẹn khám/xét nghiệm đang hoạt động.");

        var now = _clock.GetUtcNow().UtcDateTime;

        var ok = await _packages.SoftDeleteAsync(existing.Id, now, userId, ct);
        if (!ok) throw new ServicePackageNotFoundException();

        _logger.LogInformation("Service package deleted: {Id} ({Code}) by {UserId}", existing.Id, existing.Code, userId);

        if (_auditLogs != null)
        {
            try
            {
                await _auditLogs.AddAsync(new AuditLog
                {
                    AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = now,
                    UserId = userId,
                    UserName = userId,
                    Action = "SERVICE_PACKAGE_DELETED",
                    Category = "SERVICE_PACKAGE",
                    Severity = "WARNING",
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "ServicePackage",
                    EntityId = existing.Id,
                    Details = $"Soft-deleted package {existing.Code} ('{existing.Name}'). Status set to INACTIVE. DeletedBy: {userId}"
                }, ct);
            }
            catch {  }
        }
    }

    private async Task ValidateTestsAsync(List<ServicePackageTestDto>? tests, CancellationToken ct)
    {
        if (tests == null || tests.Count == 0) return;

        if (_testCriteria != null)
        {
            foreach (var test in tests)
            {
                var code = test.Code?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new DomainValidationException("VALIDATION_ERROR", "Mã xét nghiệm trong gói không được để trống.");
                }

                var criterion = await _testCriteria.GetByCodeAsync(code, ct);
                if (criterion is null || !criterion.IsActive)
                {
                    throw new DomainValidationException("TEST_NOT_FOUND", $"Mã xét nghiệm '{test.Code}' không tồn tại trong hệ thống hoặc đã ngừng áp dụng.");
                }
            }
        }
    }

    private async Task<PagedResult<ServicePackageDto>> QueryAsync(
        ServicePackageListRequest r, string? keyword, decimal? minPrice, decimal? maxPrice,
        bool includeInactive, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(keyword) && keyword.Trim().Length > 100)
        {
            throw new DomainValidationException("KEYWORD_TOO_LONG", "Từ khóa tìm kiếm không được vượt quá 100 ký tự.");
        }

        if (r.PageSize > 100 || r.PageSize < 1)
        {
            throw new DomainValidationException("INVALID_PAGE_SIZE", "Kích thước trang phải từ 1 đến 100.");
        }

        var sortBy = r.SortBy is not null && SortFields.Contains(r.SortBy)
            ? SortFields.First(f => f.Equals(r.SortBy, StringComparison.OrdinalIgnoreCase))
            : "createdAt";

        var desc = r.SortDir is null
            ? sortBy == "createdAt"
            : r.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

        var query = new ServicePackageQuery(
            Keyword: string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(),
            IsActive: includeInactive ? r.IsActive : true,
            MinPrice: minPrice,
            MaxPrice: maxPrice,
            SortBy: sortBy,
            SortDesc: desc,
            Page: r.Page,
            PageSize: r.PageSize,
            Category: string.IsNullOrWhiteSpace(r.Category) ? null : r.Category.Trim());

        var page = await _packages.SearchAsync(query, ct);
        return new PagedResult<ServicePackageDto>(
            page.Items.Select(p => ToDto(p, canManage: includeInactive)).ToList(), page.Page, page.PageSize, page.TotalItems);
    }

    private static void Apply(ServicePackage e, ServicePackageWriteRequest r)
    {
        e.Name = r.Name.Trim();
        e.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        if (r.Category is not null)
            e.Category = string.IsNullOrWhiteSpace(r.Category) ? null : r.Category.Trim();
        if (r.PreparationNotes is not null)
            e.PreparationNotes = string.IsNullOrWhiteSpace(r.PreparationNotes) ? null : r.PreparationNotes.Trim();
        if (r.TargetAudience is not null)
            e.TargetAudience = string.IsNullOrWhiteSpace(r.TargetAudience) ? null : r.TargetAudience.Trim();
        if (r.Tat is not null)
            e.Tat = string.IsNullOrWhiteSpace(r.Tat) ? null : r.Tat.Trim();
        if (r.SampleTube is not null)
            e.SampleTube = string.IsNullOrWhiteSpace(r.SampleTube) ? null : r.SampleTube.Trim();
        if (r.SampleVolumeMl is not null)
            e.SampleVolumeMl = r.SampleVolumeMl;
        if (r.Highlights is not null)
            e.Highlights = r.Highlights.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToList();

        e.Price = r.Price;
        e.DiscountPrice = r.DiscountPrice;
        e.CostPrice = r.CostPrice;
        e.InternalNotes = string.IsNullOrWhiteSpace(r.InternalNotes) ? null : r.InternalNotes.Trim();
        e.IsActive = r.IsActive;
        e.Tests = r.Tests
            .Select(t => new ServicePackageTest
            {
                Code = t.Code.Trim().ToUpperInvariant(),
                Name = t.Name.Trim(),
                SampleType = t.SampleType?.Trim(),
                Price = t.Price,
                ClinicalMeaning = t.ClinicalMeaning?.Trim()
            })
            .ToList();
    }

    private static ServicePackageDto ToDto(ServicePackage p, bool canManage) => new(
        p.Id, p.Code, p.Name, p.Description, p.Price, p.DiscountPrice,
        p.Tests.Select(t => new ServicePackageTestInfo(t.Code, t.Name, t.SampleType, t.Price, t.ClinicalMeaning)).ToList(),
        p.IsActive, p.CreatedAt, p.UpdatedAt, p.Category, p.PreparationNotes,
        p.TargetAudience, p.Tat, p.SampleTube, p.SampleVolumeMl, p.Highlights,

        CostPrice: canManage ? p.CostPrice : null,
        InternalNotes: canManage ? p.InternalNotes : null,
        CreatedBy: canManage ? p.CreatedBy : null,
        UpdatedBy: canManage ? p.UpdatedBy : null);
}
