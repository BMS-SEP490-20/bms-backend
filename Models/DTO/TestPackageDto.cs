namespace TestOrder.Models.DTO
{
    public class TestPackageDto
    {
        public string PackageId { get; set; } = null!;
        public string PackageName { get; set; } = null!;
        public string? Description { get; set; }
        public decimal? Price { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
