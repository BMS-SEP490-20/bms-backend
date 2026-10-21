using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Query filters for specimen history retrieval (UC70).
/// </summary>
public class SpecimenHistoryQueryParameters
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 15;
    public string? Keyword { get; set; }
    public string? SampleType { get; set; }
    public string? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// DTO representing an event milestone in specimen lifecycle.
/// </summary>
public class SpecimenTimelineEventDto
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string OperatorRole { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? TemperatureC { get; set; }
    public double? HumidityPercent { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for cold chain and archive storage information (UC70).
/// </summary>
public class SpecimenStorageInfoDto
{
    public string StorageStatus { get; set; } = "Stored";
    public string StorageUnit { get; set; } = string.Empty;
    public string RackId { get; set; } = string.Empty;
    public string SlotPosition { get; set; } = string.Empty;
    public double TargetTemperatureMin { get; set; } = 2.0;
    public double TargetTemperatureMax { get; set; } = 8.0;
    public double CurrentTemperature { get; set; } = 4.0;
    public double CurrentHumidity { get; set; } = 45.0;
    public bool IsTemperatureCompliant { get; set; } = true;
    public string PreservationMethod { get; set; } = string.Empty;
    public string StoredAt { get; set; } = string.Empty;
    public string StoredBy { get; set; } = string.Empty;
    public int RetentionPeriodDays { get; set; } = 7;
    public string ExpirationDate { get; set; } = string.Empty;
    public string DisposalDueDate { get; set; } = string.Empty;
    public string DisposalStatus { get; set; } = "Retained";
    public int RemainingDays { get; set; } = 7;
}

/// <summary>
/// Detailed specimen history record for full timeline and audit modal (UC70).
/// </summary>
public class SpecimenHistoryDetailDto
{
    public string Id { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string? RfidTag { get; set; }
    public string SampleType { get; set; } = string.Empty;
    public string TubeColor { get; set; } = string.Empty;
    public string Anticoagulant { get; set; } = string.Empty;
    public double VolumeMl { get; set; }
    public double MinimumRequiredVolumeMl { get; set; }

    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public int PatientAge { get; set; }
    public string? PatientPhone { get; set; }

    public string OrderId { get; set; } = string.Empty;
    public string? AppointmentId { get; set; }
    public string OrderDate { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string TestPackageName { get; set; } = string.Empty;
    public List<string> TestCriteriaCodes { get; set; } = [];

    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;

    public string CollectionTime { get; set; } = string.Empty;
    public string CollectorId { get; set; } = string.Empty;
    public string CollectorName { get; set; } = string.Empty;
    public string CollectionSite { get; set; } = string.Empty;

    public List<SpecimenTimelineEventDto> Timeline { get; set; } = [];
    public SpecimenStorageInfoDto Storage { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
}

/// <summary>
/// Table row summary DTO for specimen history listing.
/// </summary>
public class SpecimenHistoryListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string? RfidTag { get; set; }
    public string SampleType { get; set; } = string.Empty;
    public string TubeColor { get; set; } = string.Empty;
    public double VolumeMl { get; set; }

    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public int PatientAge { get; set; }

    public string OrderId { get; set; } = string.Empty;
    public string TestPackageName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string CollectionTime { get; set; } = string.Empty;

    public string StorageUnit { get; set; } = string.Empty;
    public string RackId { get; set; } = string.Empty;
    public string SlotPosition { get; set; } = string.Empty;
    public double CurrentTemperature { get; set; }
    public int RemainingDays { get; set; }
}

/// <summary>
/// Request payload to append a tracking event to a specimen (UC70).
/// </summary>
public class AddSpecimenTrackingEventDto
{
    [Required(ErrorMessage = "Loại sự kiện (EventType) là bắt buộc.")]
    public string EventType { get; set; } = "InTransit";

    [Required(ErrorMessage = "Tiêu đề sự kiện (Title) là bắt buộc.")]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Location { get; set; }

    public string? OperatorName { get; set; }

    public string? OperatorRole { get; set; }

    public string? Status { get; set; }

    public double? TemperatureC { get; set; }

    public double? HumidityPercent { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// Request payload to update storage location and preservation conditions (UC70).
/// </summary>
public class UpdateSpecimenStorageDto
{
    [Required(ErrorMessage = "Đơn vị lưu trữ (StorageUnit) là bắt buộc.")]
    public string StorageUnit { get; set; } = "Tủ Mát Bảo Quản Mẫu Lab #02 (2°C - 8°C)";

    [Required(ErrorMessage = "Mã giá đỡ (RackId) là bắt buộc.")]
    public string RackId { get; set; } = "RACK-BIO-01";

    [Required(ErrorMessage = "Vị trí ô (SlotPosition) là bắt buộc.")]
    public string SlotPosition { get; set; } = "Khay A - Ô 01";

    public double TargetTemperatureMin { get; set; } = 2.0;
    public double TargetTemperatureMax { get; set; } = 8.0;
    public double CurrentTemperature { get; set; } = 4.0;
    public double CurrentHumidity { get; set; } = 45.0;

    public string? PreservationMethod { get; set; }
    public int RetentionPeriodDays { get; set; } = 7;
    public string? StorageStatus { get; set; } = "Stored";
}
