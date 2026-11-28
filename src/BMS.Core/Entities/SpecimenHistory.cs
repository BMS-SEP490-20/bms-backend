using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Individual chronological milestone in specimen lifecycle (UC70 / Retrieve Specimen History).
/// Tracks collection, transit, reception, centrifugation, analysis, storage, and disposal.
/// </summary>
[BsonIgnoreExtraElements]
public class SpecimenTimelineEvent
{
    [BsonElement("eventId")]
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");

    [BsonElement("eventType")]
    public string EventType { get; set; } = "OrderCreated";

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [BsonElement("operatorId")]
    public string OperatorId { get; set; } = string.Empty;

    [BsonElement("operatorName")]
    public string OperatorName { get; set; } = string.Empty;

    [BsonElement("operatorRole")]
    public string OperatorRole { get; set; } = string.Empty;

    [BsonElement("location")]
    public string Location { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("temperatureC")]
    public double? TemperatureC { get; set; }

    [BsonElement("humidityPercent")]
    public double? HumidityPercent { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// Specimen preservation and physical archive storage metadata (ISO 15189 / Decision 2429/QD-BYT).
/// Populated strictly from persisted DB records — never pre-filled with synthetic defaults.
/// </summary>
[BsonIgnoreExtraElements]
public class SpecimenStorageInfo
{
    [BsonElement("storageStatus")]
    public string StorageStatus { get; set; } = string.Empty;

    [BsonElement("storageUnit")]
    public string StorageUnit { get; set; } = string.Empty;

    [BsonElement("rackId")]
    public string RackId { get; set; } = string.Empty;

    [BsonElement("slotPosition")]
    public string SlotPosition { get; set; } = string.Empty;

    [BsonElement("targetTemperatureMin")]
    public double? TargetTemperatureMin { get; set; }

    [BsonElement("targetTemperatureMax")]
    public double? TargetTemperatureMax { get; set; }

    [BsonElement("currentTemperature")]
    public double? CurrentTemperature { get; set; }

    [BsonElement("currentHumidity")]
    public double? CurrentHumidity { get; set; }

    [BsonElement("isTemperatureCompliant")]
    public bool? IsTemperatureCompliant { get; set; }

    [BsonElement("preservationMethod")]
    public string PreservationMethod { get; set; } = string.Empty;

    [BsonElement("storedAt")]
    public DateTime? StoredAt { get; set; }

    [BsonElement("storedBy")]
    public string StoredBy { get; set; } = string.Empty;

    [BsonElement("retentionPeriodDays")]
    public int? RetentionPeriodDays { get; set; }

    [BsonElement("expirationDate")]
    public DateTime? ExpirationDate { get; set; }

    [BsonElement("disposalDueDate")]
    public DateTime? DisposalDueDate { get; set; }

    [BsonElement("disposalStatus")]
    public string DisposalStatus { get; set; } = string.Empty;

    [BsonElement("disposalDate")]
    public DateTime? DisposalDate { get; set; }

    [BsonElement("disposalMethod")]
    public string? DisposalMethod { get; set; }

    [BsonElement("disposalApprovedBy")]
    public string? DisposalApprovedBy { get; set; }
}

/// <summary>
/// Domain entity representing a complete specimen history tracking record (UC70 / BR-212).
/// Maps to MongoDB collection "specimen_histories".
/// Conforms to: ISO 15189:2022, Decision 2429/QD-BYT and BR-PHL-01 to BR-PHL-04.
/// </summary>
[BsonIgnoreExtraElements]
public class SpecimenHistoryRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("barcode")]
    public string Barcode { get; set; } = string.Empty;

    [BsonElement("rfidTag")]
    public string? RfidTag { get; set; }

    [BsonElement("sampleType")]
    public string SampleType { get; set; } = "Blood";

    [BsonElement("tubeColor")]
    public string TubeColor { get; set; } = "Tím (EDTA)";

    [BsonElement("anticoagulant")]
    public string Anticoagulant { get; set; } = "EDTA K2";

    [BsonElement("volumeMl")]
    public double VolumeMl { get; set; } = 2.0;

    [BsonElement("minimumRequiredVolumeMl")]
    public double MinimumRequiredVolumeMl { get; set; } = 1.0;

    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patientName")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("patientGender")]
    public string PatientGender { get; set; } = "Nam";

    [BsonElement("patientAge")]
    public int PatientAge { get; set; } = 30;

    [BsonElement("patientPhone")]
    public string? PatientPhone { get; set; }

    [BsonElement("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("appointmentId")]
    public string? AppointmentId { get; set; }

    [BsonElement("orderDate")]
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    [BsonElement("doctorId")]
    public string DoctorId { get; set; } = string.Empty;

    [BsonElement("doctorName")]
    public string DoctorName { get; set; } = string.Empty;

    [BsonElement("department")]
    public string Department { get; set; } = "Khoa Khám Bệnh";

    [BsonElement("testPackageName")]
    public string TestPackageName { get; set; } = "Tổng Phân Tích Tế Bào Máu (CBC)";

    [BsonElement("testCriteriaCodes")]
    public List<string> TestCriteriaCodes { get; set; } = [];

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("priority")]
    public string Priority { get; set; } = string.Empty;

    [BsonElement("collectionTime")]
    public DateTime CollectionTime { get; set; } = DateTime.MinValue;

    [BsonElement("collectorId")]
    public string CollectorId { get; set; } = string.Empty;

    [BsonElement("collectorName")]
    public string CollectorName { get; set; } = string.Empty;

    [BsonElement("collectionSite")]
    public string CollectionSite { get; set; } = string.Empty;

    [BsonElement("timeline")]
    public List<SpecimenTimelineEvent> Timeline { get; set; } = [];

    [BsonElement("storage")]
    public SpecimenStorageInfo? Storage { get; set; }

    [BsonElement("notes")]
    public string Notes { get; set; } = string.Empty;

    [BsonElement("rejectionReason")]
    public string? RejectionReason { get; set; }

    [BsonElement("isRejected")]
    public bool IsRejected { get; set; }

    [BsonElement("qcStatus")]
    public string? QcStatus { get; set; }

    [BsonElement("receivedBy")]
    public string? ReceivedBy { get; set; }

    [BsonElement("receivedAt")]
    public DateTime? ReceivedAt { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
