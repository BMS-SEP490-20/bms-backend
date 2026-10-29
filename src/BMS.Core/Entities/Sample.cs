using System.ComponentModel;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Lifecycle status of a biological specimen within the clinical laboratory workflow (ISO 15189).
/// </summary>
public enum SampleStatus
{
    PendingCollection = 1,
    Collected = 2,
    InTransit = 8,
    Received = 3,
    Processing = 4,
    Testing = 9,
    Completed = 5,
    Rejected = 6,
    RerunRequested = 7
}

/// <summary>
/// Clinical priority of a specimen (Routine, Urgent, or STAT emergency).
/// </summary>
public enum SamplePriority
{
    Normal = 1,
    Urgent = 2,
    Stat = 3
}

/// <summary>
/// Audit trail entry tracking sample status transitions and collection events.
/// </summary>
[BsonIgnoreExtraElements]
public class SampleStatusHistoryEntry
{
    [BsonElement("from_status")]
    public string? FromStatus { get; set; }

    [BsonElement("to_status")]
    public string ToStatus { get; set; } = string.Empty;

    [BsonElement("changed_at")]
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("changed_by")]
    public string? ChangedBy { get; set; }

    [BsonElement("operator_name")]
    public string? OperatorName { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// Represents a laboratory specimen / tube identified by a unique barcode (UC17 / Record Sample Time / Update Sample Status).
/// Maps to the <c>blood_samples</c> collection in <c>BMS_Database</c>.
/// </summary>
[BsonIgnoreExtraElements]
public class Sample : ISupportInitialize
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("barcode")]
    public string Barcode { get; set; } = string.Empty;

    [BsonElement("rfid_tag")]
    [BsonIgnoreIfNull]
    public string? RfidTag { get; set; }

    [BsonElement("appointment_id")]
    public string TestOrderId { get; set; } = string.Empty;

    [BsonIgnore]
    public string AppointmentId
    {
        get => TestOrderId;
        set => TestOrderId = value;
    }

    [BsonElement("patient_id")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patient_name")]
    [BsonIgnoreIfNull]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("patient_gender")]
    [BsonIgnoreIfNull]
    public string? PatientGender { get; set; }

    [BsonElement("patient_age")]
    [BsonIgnoreIfNull]
    public int? PatientAge { get; set; }

    [BsonElement("test_package_name")]
    [BsonIgnoreIfNull]
    public string TestPackageName { get; set; } = string.Empty;

    [BsonElement("sample_type")]
    public string SampleType { get; set; } = "Blood"; // Blood, Serum, Plasma, Urine

    [BsonElement("tube_color")]
    [BsonIgnoreIfNull]
    public string? TubeColor { get; set; }

    [BsonElement("anticoagulant")]
    [BsonIgnoreIfNull]
    public string? Anticoagulant { get; set; }

    [BsonElement("volume_ml")]
    public double VolumeMl { get; set; } = 2.0;

    [BsonElement("status")]
    public string StatusString { get; set; } = SampleStatus.Received.ToString();

    [BsonIgnore]
    public SampleStatus Status
    {
        get => ParseStatus(StatusString);
        set => StatusString = value.ToString();
    }

    [BsonElement("priority")]
    public string PriorityString { get; set; } = SamplePriority.Normal.ToString();

    [BsonIgnore]
    public SamplePriority Priority
    {
        get => Enum.TryParse<SamplePriority>(PriorityString, true, out var p) ? p : SamplePriority.Normal;
        set => PriorityString = value.ToString();
    }

    [BsonElement("lab_room")]
    [BsonIgnoreIfNull]
    public string? LabRoom { get; set; }

    [BsonElement("collected_by")]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string? CollectedBy { get; set; }

    [BsonElement("collector_name")]
    [BsonIgnoreIfNull]
    public string? CollectorName { get; set; }

    [BsonElement("assigned_technician_id")]
    [BsonIgnoreIfNull]
    public string? AssignedTechnicianId { get; set; }

    [BsonElement("assigned_technician_name")]
    [BsonIgnoreIfNull]
    public string? AssignedTechnicianName { get; set; }

    [BsonElement("rejection_reason")]
    public string? RejectionReason { get; set; }

    [BsonElement("rerun_reason")]
    [BsonIgnoreIfNull]
    public string? RerunReason { get; set; }

    [BsonElement("batch_id")]
    [BsonIgnoreIfNull]
    public string? BatchId { get; set; }

    [BsonElement("collected_at")]
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("in_transit_at")]
    [BsonIgnoreIfNull]
    public DateTime? InTransitAt { get; set; }

    [BsonElement("received_at")]
    [BsonIgnoreIfNull]
    public DateTime? ReceivedAt { get; set; }

    [BsonElement("received_by")]
    [BsonIgnoreIfNull]
    public string? ReceivedBy { get; set; }

    [BsonElement("processed_at")]
    [BsonIgnoreIfNull]
    public DateTime? ProcessedAt { get; set; }

    [BsonElement("completed_at")]
    [BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("rejected_at")]
    [BsonIgnoreIfNull]
    public DateTime? RejectedAt { get; set; }

    [BsonElement("notes")]
    [BsonIgnoreIfNull]
    public string? Notes { get; set; }

    /// <summary>Physical storage location of the tube (rack / fridge), used by Print Barcodes (#58).</summary>
    [BsonElement("storage_location")]
    [BsonIgnoreIfNull]
    public string? StorageLocation { get; set; }

    /// <summary>
    /// Soft-delete flag (<c>is_deleted</c>), already honoured by UC71 Sample Timeline and UC76 Test History.
    /// Declared explicitly so full-document replaces do not drop it (ExtraElements are cleared on load).
    /// </summary>
    [BsonElement("is_deleted")]
    [BsonIgnoreIfDefault]
    public bool IsDeleted { get; set; }

    [BsonElement("status_history")]
    public List<SampleStatusHistoryEntry> StatusHistory { get; set; } = [];

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    [BsonIgnoreIfNull]
    public DateTime? UpdatedAt { get; set; }

    [BsonExtraElements]
    public BsonDocument? ExtraElements { get; set; }

    public static SampleStatus ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return SampleStatus.PendingCollection;
        if (Enum.TryParse<SampleStatus>(raw.Trim(), true, out var parsed)) return parsed;
        return SampleStatus.Received;
    }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
        ExtraElements = null;
    }
}
