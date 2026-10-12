using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.ServicePackages;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

public sealed class ServicePackageService(
    IServicePackageRepository packages,
    TimeProvider clock,
    ILogger<ServicePackageService> logger) : IServicePackageService
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
        { "name", "code", "price", "createdAt" };

    public Task<PagedResult<ServicePackageDto>> ListAsync(
        ServicePackageListRequest request, bool includeInactive, CancellationToken ct = default)
        => QueryAsync(request, keyword: null, minPrice: null, maxPrice: null, includeInactive, ct);

    public Task<PagedResult<ServicePackageDto>> SearchAsync(
        ServicePackageSearchRequest request, bool includeInactive, CancellationToken ct = default)
        => QueryAsync(request, request.Keyword, request.MinPrice, request.MaxPrice, includeInactive, ct);

    public async Task<ServicePackageDto> GetByIdAsync(string id, bool includeInactive, CancellationToken ct = default)
    {
        var p = await packages.GetByIdAsync(id, ct);
        // Gói đang ẩn coi như không tồn tại với người không có quyền quản lý
        if (p is null || (!includeInactive && !p.IsActive))
            throw new ServicePackageNotFoundException();
        return ToDto(p);
    }

    public async Task<ServicePackageDto> CreateAsync(CreateServicePackageRequest request, string userId, CancellationToken ct = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await packages.CodeExistsAsync(code, ct))
            throw new ServicePackageCodeAlreadyExistsException(code);

        var entity = new ServicePackage
        {
            Code = code,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            CreatedBy = userId
        };
        Apply(entity, request);

        await packages.InsertAsync(entity, ct); // bắt thêm trường hợp 2 request tạo trùng mã cùng lúc
        logger.LogInformation("Service package created: {Id} ({Code}) by {UserId}", entity.Id, entity.Code, userId);
        return ToDto(entity);
    }

    public async Task<ServicePackageDto> UpdateAsync(string id, UpdateServicePackageRequest request, string userId, CancellationToken ct = default)
    {
        var entity = await packages.GetByIdAsync(id, ct) ?? throw new ServicePackageNotFoundException();

        Apply(entity, request);
        entity.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        entity.UpdatedBy = userId;

        if (!await packages.ReplaceAsync(entity, ct))
            throw new ServicePackageNotFoundException(); // vừa bị xoá bởi request khác

        logger.LogInformation("Service package updated: {Id} by {UserId}", entity.Id, userId);
        return ToDto(entity);
    }

    public async Task DeleteAsync(string id, string userId, CancellationToken ct = default)
    {
        var ok = await packages.SoftDeleteAsync(id, clock.GetUtcNow().UtcDateTime, userId, ct);
        if (!ok) throw new ServicePackageNotFoundException();
        logger.LogInformation("Service package deleted: {Id} by {UserId}", id, userId);
    }

    // ---------------------------------------------------------------------

    private async Task<PagedResult<ServicePackageDto>> QueryAsync(
        ServicePackageListRequest r, string? keyword, decimal? minPrice, decimal? maxPrice,
        bool includeInactive, CancellationToken ct)
    {
        var sortBy = r.SortBy is not null && SortFields.Contains(r.SortBy)
            ? SortFields.First(f => f.Equals(r.SortBy, StringComparison.OrdinalIgnoreCase))
            : "createdAt";

        var desc = r.SortDir is null
            ? sortBy == "createdAt"
            : r.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

        var query = new ServicePackageQuery(
            Keyword: string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(),
            IsActive: includeInactive ? r.IsActive : true, // người thường chỉ thấy gói đang hoạt động
            MinPrice: minPrice,
            MaxPrice: maxPrice,
            SortBy: sortBy,
            SortDesc: desc,
            Page: r.Page,
            PageSize: r.PageSize);

        var page = await packages.SearchAsync(query, ct);
        return new PagedResult<ServicePackageDto>(
            page.Items.Select(ToDto).ToList(), page.Page, page.PageSize, page.TotalItems);
    }

    private static void Apply(ServicePackage e, ServicePackageWriteRequest r)
    {
        e.Name = r.Name.Trim();
        e.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        e.Price = r.Price;
        e.DiscountPrice = r.DiscountPrice;
        e.IsActive = r.IsActive;
        e.Tests = r.Tests
            .Select(t => new ServicePackageTest { Code = t.Code.Trim().ToUpperInvariant(), Name = t.Name.Trim() })
            .ToList();
    }

    private static ServicePackageDto ToDto(ServicePackage p) => new(
        p.Id, p.Code, p.Name, p.Description, p.Price, p.DiscountPrice,
        p.Tests.Select(t => new ServicePackageTestInfo(t.Code, t.Name)).ToList(),
        p.IsActive, p.CreatedAt, p.UpdatedAt);
}
