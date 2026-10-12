using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.ServicePackages;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

/// <summary>Điều kiện truy vấn đã được chuẩn hoá (không còn phụ thuộc HTTP).</summary>
public sealed record ServicePackageQuery(
    string? Keyword,
    bool? IsActive,
    decimal? MinPrice,
    decimal? MaxPrice,
    string SortBy,
    bool SortDesc,
    int Page,
    int PageSize);

public interface IServicePackageRepository
{
    Task<ServicePackage?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<PagedResult<ServicePackage>> SearchAsync(ServicePackageQuery query, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);

    /// <summary>Ném ServicePackageCodeAlreadyExistsException nếu trùng mã (kể cả khi 2 request đua nhau).</summary>
    Task InsertAsync(ServicePackage package, CancellationToken ct = default);

    /// <summary>Ghi đè document; trả false nếu gói không tồn tại hoặc đã bị xoá.</summary>
    Task<bool> ReplaceAsync(ServicePackage package, CancellationToken ct = default);

    /// <summary>Xoá mềm; trả false nếu không tìm thấy / đã xoá trước đó.</summary>
    Task<bool> SoftDeleteAsync(string id, DateTime now, string? userId, CancellationToken ct = default);
}

public interface IServicePackageService
{
    Task<PagedResult<ServicePackageDto>> ListAsync(ServicePackageListRequest request, bool includeInactive, CancellationToken ct = default);
    Task<PagedResult<ServicePackageDto>> SearchAsync(ServicePackageSearchRequest request, bool includeInactive, CancellationToken ct = default);
    Task<ServicePackageDto> GetByIdAsync(string id, bool includeInactive, CancellationToken ct = default);
    Task<ServicePackageDto> CreateAsync(CreateServicePackageRequest request, string userId, CancellationToken ct = default);
    Task<ServicePackageDto> UpdateAsync(string id, UpdateServicePackageRequest request, string userId, CancellationToken ct = default);
    Task DeleteAsync(string id, string userId, CancellationToken ct = default);
}
