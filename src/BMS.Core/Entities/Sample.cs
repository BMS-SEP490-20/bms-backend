namespace BMS.Core.Entities;

public enum SampleStatus
{
    PendingCollection = 1,
    Collected = 2,
    Received = 3,
    Processing = 4,
    Completed = 5,
    Rejected = 6,
    RerunRequested = 7
}

public enum SamplePriority
{
    Normal = 1,
    Urgent = 2,
    Stat = 3
}

public class Sample
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Barcode { get; set; } = string.Empty;
    public string TestOrderId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientGender { get; set; }
    public int? PatientAge { get; set; }
    public string TestPackageName { get; set; } = string.Empty;
    public string SampleType { get; set; } = "Blood"; // Blood, Serum, Plasma, Urine
    public SampleStatus Status { get; set; } = SampleStatus.Received;
    public SamplePriority Priority { get; set; } = SamplePriority.Normal;
    public string? LabRoom { get; set; }
    public string? AssignedTechnicianId { get; set; }
    public string? AssignedTechnicianName { get; set; }
    public string? RejectionReason { get; set; }
    public string? RerunReason { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }
}
