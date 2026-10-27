namespace TestOrder.Models.DTO
{
    public class BloodSampleReadDto
    {
        public string SampleId { get; set; } = null!;
        public string AppointmentId { get; set; } = null!;
        public string SampleType { get; set; } = null!;
        public DateTime? CollectedAt { get; set; }
        public decimal? Volume { get; set; }
        public string? Status { get; set; }
        public string? StorageLocation { get; set; }
        public string? Notes { get; set; }
        public string? LabUserId { get; set; }
        public string Barcode => SampleId;
        public string? CollectionTimestamp => CollectedAt?.ToString("yyyy-MM-dd HH:mm");
    }
}
