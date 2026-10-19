using System.ComponentModel.DataAnnotations;

namespace BMS.SampleCheckIn.DTOs;

public class CheckInSampleRequest
{
    /// <summary>Barcode (BC-…) or SampleId (SMP-…).</summary>
    [Required]
    public string Code { get; set; } = string.Empty;

    public string? LabRoomId { get; set; }

    /// <summary>Optional rejection at check-in (BR-PHL-03).</summary>
    public string? RejectionCode { get; set; }

    public string? RejectionNote { get; set; }
}

public class SampleDto
{
    public string SampleId { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? TubeColor { get; set; }
    public string? Anticoagulant { get; set; }
    public double VolumeMl { get; set; }
    public DateTime? CollectedAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public string? LabRoomId { get; set; }
    public string? RejectionReason { get; set; }
}

public class ApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
