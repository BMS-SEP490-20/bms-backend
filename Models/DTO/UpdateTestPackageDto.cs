namespace TestOrder.Models.DTO
{
    public class UpdateTestPackageDto
    {
        public string? PackageName { get; set; }
        public string? Description { get; set; }
        public decimal? Price { get; set; }
        public string? Status { get; set; }
        public List<string> CriteriaIds { get; set; } = new();
    }
}
