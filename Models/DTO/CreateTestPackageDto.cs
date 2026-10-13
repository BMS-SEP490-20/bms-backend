namespace TestOrder.Models.DTO
{
    public class CreateTestPackageDto
    {
        public string PackageName { get; set; } = null!;
        public string? Description { get; set; }
        public decimal? Price { get; set; }
        public string? Status { get; set; }
        public List<string> CriteriaIds { get; set; } = new();
    }
}
