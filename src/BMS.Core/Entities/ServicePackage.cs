namespace BMS.Core.Entities;

/// <summary>Một xét nghiệm nằm trong gói (nhúng trực tiếp trong document gói dịch vụ).</summary>
public class ServicePackageTest
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? SampleType { get; set; }
    public decimal? Price { get; set; }
    public string? ClinicalMeaning { get; set; }
}

/// <summary>Gói dịch vụ / gói xét nghiệm – map với collection "service_packages".</summary>
public class ServicePackage
{
    public string Id { get; set; } = default!;

    /// <summary>Mã gói (duy nhất trong các gói chưa xoá), luôn viết hoa.</summary>
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? PreparationNotes { get; set; }

    /// <summary>Đối tượng cần làm xét nghiệm (chỉ định lâm sàng).</summary>
    public string? TargetAudience { get; set; }

    /// <summary>Thời gian cam kết trả kết quả (TAT - Turnaround Time).</summary>
    public string? Tat { get; set; }

    /// <summary>Loại ống lấy mẫu bệnh phẩm theo chuẩn ISO 15189 (EDTA tím, Serum đỏ/vàng, Citrate xanh...).</summary>
    public string? SampleTube { get; set; }

    /// <summary>Thể tích máu tối thiểu yêu cầu (ml).</summary>
    public double? SampleVolumeMl { get; set; }

    /// <summary>Các điểm nổi bật / cam kết chất lượng (Shield icons).</summary>
    public List<string> Highlights { get; set; } = new();

    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }

    public List<ServicePackageTest> Tests { get; set; } = new();

    /// <summary>Đang được bán/cho phép đặt hay không.</summary>
    public bool IsActive { get; set; } = true;

    // --- Xoá mềm: giữ lại để hoá đơn/lịch sử cũ vẫn tham chiếu được ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
