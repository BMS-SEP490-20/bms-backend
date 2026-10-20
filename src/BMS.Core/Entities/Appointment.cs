using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Status lifecycle of a patient clinical appointment.
/// </summary>
public enum AppointmentStatus
{
    Pending = 0,
    Confirmed = 1,
    CheckedIn = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5,
    NoShow = 6
}

/// <summary>
/// Type of appointment booking: At clinic or home phlebotomy collection.
/// </summary>
public enum AppointmentType
{
    ClinicVisit = 1,
    HomePhlebotomy = 2
}

/// <summary>
/// Represents a scheduled clinical consultation or specimen collection appointment (UC21).
/// </summary>
public class Appointment
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("appointmentCode")]
    public string AppointmentCode { get; set; } = string.Empty;

    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("doctorId")]
    public string? DoctorId { get; set; }

    [BsonElement("packageId")]
    public string? PackageId { get; set; }

    [BsonElement("appointmentDate")]
    public DateTime AppointmentDate { get; set; }

    [BsonElement("startTime")]
    public TimeSpan StartTime { get; set; }

    [BsonElement("endTime")]
    public TimeSpan EndTime { get; set; }

    [BsonElement("type")]
    public AppointmentType Type { get; set; } = AppointmentType.ClinicVisit;

    [BsonElement("status")]
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("collectionAddress")]
    public string? CollectionAddress { get; set; }

    [BsonElement("queueNumber")]
    public int? QueueNumber { get; set; }

    [BsonElement("roomId")]
    public string? RoomId { get; set; }

    [BsonElement("recallCount")]
    public int RecallCount { get; set; } = 0;

    [BsonElement("consultationStartTime")]
    public DateTime? ConsultationStartTime { get; set; }

    [BsonElement("consultationEndTime")]
    public DateTime? ConsultationEndTime { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
