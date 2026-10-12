using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// A doctor's work shift on a specific date.
/// Maps to the <c>doctor_shifts</c> collection.
/// </summary>
public class DoctorShift
{
    /// <summary>Shift identifier.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Doctor account / profile Id.</summary>
    [BsonElement("doctor_id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string DoctorId { get; set; } = string.Empty;

    /// <summary>Room used during this shift.</summary>
    [BsonElement("room_id")]
    public string RoomId { get; set; } = string.Empty;

    /// <summary>Calendar date of the shift (UTC date portion).</summary>
    [BsonElement("shift_date")]
    public DateTime ShiftDate { get; set; }

    /// <summary>
    /// Shift type: Morning, Afternoon, Evening, FullDay.
    /// </summary>
    [BsonElement("shift_type")]
    public string ShiftType { get; set; } = "Morning";

    /// <summary>Maximum patients allowed in this shift.</summary>
    [BsonElement("max_patients")]
    public int MaxPatients { get; set; }

    /// <summary>Current booked / checked-in patient count.</summary>
    [BsonElement("current_patients_count")]
    public int CurrentPatientsCount { get; set; }

    /// <summary>
    /// Approval status: Pending, Approved, Rejected.
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = "Approved";
}