using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// A clinical or laboratory staff work shift on a specific date.
/// Maps to the <c>doctor_shifts</c> collection in <c>BMS_Database</c>.
/// </summary>
[BsonIgnoreExtraElements]
public class DoctorShift
{
    /// <summary>Shift identifier (MongoDB ObjectId string).</summary>
    [BsonId]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Doctor / Employee account ObjectId.</summary>
    [BsonElement("doctor_id")]
    [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
    public string DoctorId { get; set; } = string.Empty;

    /// <summary>Cached employee full name for display.</summary>
    [BsonElement("employee_name")]
    [BsonIgnoreIfNull]
    public string? EmployeeName { get; set; }

    /// <summary>Cached employee role / title (Doctor, Nurse, LabTechnician, Receptionist...).</summary>
    [BsonElement("employee_role")]
    [BsonIgnoreIfNull]
    public string? EmployeeRole { get; set; }

    /// <summary>Department or specialty.</summary>
    [BsonElement("department")]
    [BsonIgnoreIfNull]
    public string? Department { get; set; }

    /// <summary>Room assigned during this shift (e.g. ROOM-101, LAB-ROOM-201).</summary>
    [BsonElement("room_id")]
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Cached room name.</summary>
    [BsonElement("room_name")]
    [BsonIgnoreIfNull]
    public string? RoomName { get; set; }

    /// <summary>Calendar date of the shift (UTC date portion).</summary>
    [BsonElement("shift_date")]
    public DateTime ShiftDate { get; set; }

    /// <summary>
    /// Shift type: Morning, Afternoon, Evening, Night, FullDay.
    /// </summary>
    [BsonElement("shift_type")]
    public string ShiftType { get; set; } = "Morning";

    /// <summary>Shift start time in HH:mm (24h format, e.g. "07:00").</summary>
    [BsonElement("shift_start")]
    [BsonIgnoreIfNull]
    public string? ShiftStart { get; set; }

    /// <summary>Shift end time in HH:mm (24h format, e.g. "11:30").</summary>
    [BsonElement("shift_end")]
    [BsonIgnoreIfNull]
    public string? ShiftEnd { get; set; }

    /// <summary>Maximum patients allowed in this shift.</summary>
    [BsonElement("max_patients")]
    public int MaxPatients { get; set; } = 30;

    /// <summary>Current booked / checked-in patient count.</summary>
    [BsonElement("current_patients_count")]
    public int CurrentPatientsCount { get; set; }

    /// <summary>
    /// Shift status: Approved, Scheduled, Active, Completed, Cancelled, Pending, Rejected.
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = "Approved";

    /// <summary>Optional shift notes or handover remarks.</summary>
    [BsonElement("notes")]
    [BsonIgnoreIfNull]
    public string? Notes { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    [BsonIgnoreIfNull]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("created_by")]
    [BsonIgnoreIfNull]
    public string? CreatedBy { get; set; }

    [BsonElement("updated_by")]
    [BsonIgnoreIfNull]
    public string? UpdatedBy { get; set; }
}