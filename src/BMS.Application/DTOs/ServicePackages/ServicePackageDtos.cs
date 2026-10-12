using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.ServicePackages;

public sealed class ServicePackageTestDto
{
    [Required(ErrorMessage = "Mã xét nghiệm là bắt buộc.")]
    [StringLength(50, MinimumLength = 1)]
    public string Code { get; init; } = default!;

    [Required(ErrorMessage = "Tên xét nghiệm là bắt buộc.")]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = default!;
}

/// <summary>Phần dữ liệu dùng chung cho Create / Update.</summary>
public abstract class ServicePackageWriteRequest : IValidatableObject
{
    [Required(ErrorMessage = "Vui lòng nhập tên gói dịch vụ.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Tên gói phải từ 2 đến 200 ký tự.")]
    public string Name { get; init; } = default!;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Range(typeof(decimal), "0", "1000000000", ErrorMessage = "Giá không hợp lệ.")]
    public decimal Price { get; init; }

    [Range(typeof(decimal), "0", "1000000000", ErrorMessage = "Giá khuyến mãi không hợp lệ.")]
    public decimal? DiscountPrice { get; init; }

    [MaxLength(100, ErrorMessage = "Một gói tối đa 100 xét nghiệm.")]
    public List<ServicePackageTestDto> Tests { get; init; } = new();

    public bool IsActive { get; init; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DiscountPrice is { } d && d > Price)
            yield return new ValidationResult("Giá khuyến mãi không được lớn hơn giá gốc.", [nameof(DiscountPrice)]);

        if (Tests.Select(t => t.Code?.Trim().ToUpperInvariant()).Distinct().Count() != Tests.Count)
            yield return new ValidationResult("Danh sách xét nghiệm có mã bị trùng.", [nameof(Tests)]);
    }
}

public sealed class CreateServicePackageRequest : ServicePackageWriteRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mã gói dịch vụ.")]
    [StringLength(50, MinimumLength = 2)]
    [RegularExpression(@"^[A-Za-z0-9_\-]+$", ErrorMessage = "Mã chỉ gồm chữ, số, gạch ngang, gạch dưới.")]
    public string Code { get; init; } = default!;
}

/// <summary>Mã gói là định danh nên không cho đổi khi cập nhật.</summary>
public sealed class UpdateServicePackageRequest : ServicePackageWriteRequest;

public class ServicePackageListRequest
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;

    /// <summary>true/false: lọc theo trạng thái. Người không có quyền quản lý luôn chỉ thấy gói đang hoạt động.</summary>
    public bool? IsActive { get; init; }

    /// <summary>name | code | price | createdAt (mặc định createdAt).</summary>
    public string? SortBy { get; init; }

    /// <summary>asc | desc. Mặc định: createdAt giảm dần, còn lại tăng dần.</summary>
    [RegularExpression("(?i)^(asc|desc)$", ErrorMessage = "SortDir chỉ nhận asc hoặc desc.")]
    public string? SortDir { get; init; }
}

public sealed class ServicePackageSearchRequest : ServicePackageListRequest, IValidatableObject
{
    /// <summary>Tìm theo mã, tên, mô tả hoặc tên/mã xét nghiệm trong gói (không phân biệt hoa thường).</summary>
    [StringLength(100)]
    public string? Keyword { get; init; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal? MinPrice { get; init; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal? MaxPrice { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice is { } min && MaxPrice is { } max && min > max)
            yield return new ValidationResult("MinPrice không được lớn hơn MaxPrice.", [nameof(MinPrice)]);
    }
}

public sealed record ServicePackageDto(
    string Id,
    string Code,
    string Name,
    string? Description,
    decimal Price,
    decimal? DiscountPrice,
    IReadOnlyList<ServicePackageTestInfo> Tests,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record ServicePackageTestInfo(string Code, string Name);
