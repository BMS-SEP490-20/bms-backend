using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs;

public class SampleStatusHistoryDto
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? ChangedBy { get; set; }
    public string? OperatorName { get; set; }
    public string? Notes { get; set; }
}

public class LabQueueItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string? RfidTag { get; set; }
    public string TestOrderId { get; set; } = string.Empty;
    public string AppointmentId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientGender { get; set; }
    public int? PatientAge { get; set; }
    public string TestPackageName { get; set; } = string.Empty;
    /// <summary>Id gói catalog để FE nạp criteria (PKG-CBC-01…).</summary>
    public string? PackageId { get; set; }
    public string? PackageCode { get; set; }
    public IReadOnlyList<SampleExpectedParameterDto> PackageParameters { get; set; } = Array.Empty<SampleExpectedParameterDto>();
    public string SampleType { get; set; } = "Blood"; // Blood, Serum, Plasma...
    public string? TubeColor { get; set; }
    public string? Anticoagulant { get; set; }
    public double VolumeMl { get; set; } = 2.0;
    public string Status { get; set; } = string.Empty; // PendingCollection, Collected, InTransit, Received, Processing, Testing, Completed, Rejected, RerunRequested
    public string Priority { get; set; } = "Normal"; // Normal, Urgent, Stat
    public string? LabRoom { get; set; }
    public string? CollectedBy { get; set; }
    public string? CollectorName { get; set; }
    public string? AssignedTechnicianId { get; set; }
    public string? AssignedTechnicianName { get; set; }
    public string? ReceivedBy { get; set; }
    public string? HandoverBy { get; set; }
    public string? HandoverById { get; set; }
    public string? RejectionReason { get; set; }
    public string? RerunReason { get; set; }
    public DateTime CollectedAt { get; set; }
    public DateTime? InTransitAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectedBy { get; set; }
    public string? Notes { get; set; }
    public string? ParentSampleId { get; set; }
    public string? ParentBarcode { get; set; }
    public bool IsAliquot { get; set; }
    public IReadOnlyList<SampleStatusHistoryDto> StatusHistory { get; set; } = Array.Empty<SampleStatusHistoryDto>();
}

public class CreateAliquotRequestDto
{
    [Range(0.1, 50.0, ErrorMessage = "Thể tích ống chiết (VolumeMl) phải từ 0.1 đến 50 ml.")]
    public double VolumeMl { get; set; } = 1.0;

    [StringLength(100)]
    public string? TargetLabRoom { get; set; }

    [StringLength(50)]
    public string? SampleType { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// Request payload for recording sample collection time and specimen metadata (Record Sample Time use case).
/// </summary>
public class RecordSampleCollectionRequest
{
    /// <summary>Optional Sample ID or Barcode when calling POST /api/laboratory/samples/record-collection.</summary>
    [StringLength(100)]
    public string? SampleId { get; set; }

    /// <summary>Unique specimen barcode (auto-generated if creating a new sample and omitted).</summary>
    [StringLength(50)]
    public string? Barcode { get; set; }

    /// <summary>Optional RFID tag attached to the specimen tube (e.g. RFID-E280-0001).</summary>
    [StringLength(100)]
    public string? RfidTag { get; set; }

    /// <summary>Associated patient ID (e.g. PAT-079095001234).</summary>
    [StringLength(100)]
    public string? PatientId { get; set; }

    /// <summary>Associated appointment / test order ID (e.g. APP-20260529-01).</summary>
    [StringLength(100)]
    public string? AppointmentId { get; set; }

    /// <summary>Exact timestamp when blood/specimen was drawn (UTC or ISO-8601). Defaults to current server UTC time if omitted.</summary>
    public DateTime? CollectedAt { get; set; }

    /// <summary>Collector / Phlebotomist user ID (defaults to authenticated user ID).</summary>
    [StringLength(100)]
    public string? CollectedBy { get; set; }

    /// <summary>Collector display name.</summary>
    [StringLength(150)]
    public string? CollectorName { get; set; }

    /// <summary>Specimen type: Blood, Serum, Plasma, Urine (default: Blood).</summary>
    [StringLength(50)]
    public string? SampleType { get; set; }

    /// <summary>Tube cap color (e.g., Purple, Green, Red, Blue).</summary>
    [StringLength(50)]
    public string? TubeColor { get; set; }

    /// <summary>Anticoagulant additive (e.g., EDTA, Lithium Heparin, Sodium Citrate).</summary>
    [StringLength(100)]
    public string? Anticoagulant { get; set; }

    /// <summary>Collected specimen volume in milliliters (ml).</summary>
    [Range(0.1, 50.0, ErrorMessage = "Thể tích mẫu (VolumeMl) phải từ 0.1 đến 50 ml.")]
    public double? VolumeMl { get; set; }

    /// <summary>Clinical priority: Normal, Urgent, Stat.</summary>
    [StringLength(30)]
    public string? Priority { get; set; }

    /// <summary>Target laboratory room (e.g. LAB-ROOM-201).</summary>
    [StringLength(100)]
    public string? LabRoom { get; set; }

    /// <summary>Optional collection notes.</summary>
    [StringLength(1000)]
    public string? Notes { get; set; }
}

/// <summary>
/// Request payload for updating specimen lifecycle status (Update Sample Status use case).
/// </summary>
public class UpdateSampleStatusDto
{
    [Required(ErrorMessage = "Trạng thái mẫu mới (Status) là bắt buộc.")]
    [StringLength(50)]
    public string Status { get; set; } = string.Empty;

    [StringLength(100)]
    public string? TechnicianId { get; set; }

    [StringLength(150)]
    public string? TechnicianName { get; set; }

    [StringLength(150)]
    public string? HandoverBy { get; set; }

    [StringLength(100)]
    public string? HandoverById { get; set; }

    [StringLength(100)]
    public string? LabRoom { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    [StringLength(500)]
    public string? RejectionReason { get; set; }

    /// <summary>Optional timestamp of the status transition (defaults to current UTC time).</summary>
    public DateTime? Timestamp { get; set; }
}

public class SampleRerunRequestDto
{
    [Required(ErrorMessage = "Lý do yêu cầu chạy lại là bắt buộc.")]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    [StringLength(150)]
    public string RequestedBy { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Notes { get; set; }

    public string? Analyzer { get; set; }
    public string? Priority { get; set; }
    public List<string>? Parameters { get; set; }
}

public class SampleRerunItemDto
{
    public string SpecimenId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "New";
    public string? OriginalValue { get; set; }
    public string? RerunValue { get; set; }
    public DateTime RequestedAt { get; set; }
}

public class SampleRerunDetailsDto
{
    public string SampleId { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string CurrentResult { get; set; } = string.Empty;
    public string Flag { get; set; } = string.Empty;
    public string Analyzer { get; set; } = string.Empty;
    public string FirstRunTime { get; set; } = string.Empty;
    public List<string> AvailableParameters { get; set; } = new();
}

public class QueueFilterDto
{
    public string? Status { get; set; }
    public string? Search { get; set; } // barcode, sample ID, patient ID, or patient name
    public string? Priority { get; set; }
    public string? LabRoom { get; set; }
    public string? PatientId { get; set; }
    public string? AppointmentId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class SampleExpectedParameterDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double? NormalMin { get; set; }
    public double? NormalMax { get; set; }
    public string? NormalText { get; set; }
    public double? PanicLow { get; set; }
    public double? PanicHigh { get; set; }
    public string ValueType { get; set; } = "Numeric";
}

/// <summary>
/// Active clinical/laboratory staff member returned from MongoDB for specimen collection and check-in selectors (UC40/UC41).
/// </summary>
public class LabStaffMemberDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string? AssignedRoom { get; set; }
}
