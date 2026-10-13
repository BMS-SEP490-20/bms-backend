namespace BMS.Application.DTOs;

public class LabQueueItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string TestOrderId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientGender { get; set; }
    public int? PatientAge { get; set; }
    public string TestPackageName { get; set; } = string.Empty;
    public string SampleType { get; set; } = string.Empty; // Blood, Serum, Plasma...
    public string Status { get; set; } = string.Empty; // Received, Processing, Completed, Rejected, RerunRequested
    public string Priority { get; set; } = string.Empty; // Normal, Urgent, Stat
    public string? LabRoom { get; set; }
    public string? AssignedTechnicianName { get; set; }
    public DateTime CollectedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string? Notes { get; set; }
}

public class UpdateSampleStatusDto
{
    public string Status { get; set; } = string.Empty;
    public string? TechnicianId { get; set; }
    public string? TechnicianName { get; set; }
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
}

public class SampleRerunRequestDto
{
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class QueueFilterDto
{
    public string? Status { get; set; }
    public string? Search { get; set; } // barcode or patient name
    public string? Priority { get; set; }
    public string? LabRoom { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
