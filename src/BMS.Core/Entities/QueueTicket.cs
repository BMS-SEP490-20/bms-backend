using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Clinical priority of patient in queue (Routine, Urgent, STAT emergency).
/// </summary>
public enum QueuePriority
{
    /// <summary>Normal patient, first come first served.</summary>
    Routine = 1,
    /// <summary>Policy priority (≥80 years, pregnancy ≥7 months, infant &lt;12 months); alternates 1:1 with Routine (BA:L33).</summary>
    Urgent = 2,
    /// <summary>Emergency; always called before everyone else.</summary>
    STAT = 3
}

/// <summary>
/// Lifecycle status of queue ticket in reception and phlebotomy room.
/// </summary>
public enum QueueTicketStatus
{
    Waiting = 1,
    Called = 2,
    Collecting = 3,
    Completed = 4,
    Skipped = 5
}

/// <summary>
/// Represents a reception queue ticket issued for blood collection (UC27).
/// Stored in MongoDB collection "queue_tickets".
/// </summary>
public class QueueTicket
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("ticketNumber")]
    public string TicketNumber { get; set; } = string.Empty;

    /// <summary>Clinic-local business day (yyyy-MM-dd, +07:00) the number belongs to; numbers reset daily.</summary>
    [BsonElement("queueDate")]
    public string QueueDate { get; set; } = string.Empty;

    /// <summary>Numeric part of <see cref="TicketNumber"/> (1–999), from the atomic daily counter.</summary>
    [BsonElement("sequence")]
    public int Sequence { get; set; }

    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patientName")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("patientCode")]
    public string PatientCode { get; set; } = string.Empty;

    [BsonElement("packageName")]
    public string PackageName { get; set; } = string.Empty;

    [BsonElement("isWalkIn")]
    public bool IsWalkIn { get; set; } = false;

    [BsonElement("priority")]
    public QueuePriority Priority { get; set; } = QueuePriority.Routine;

    [BsonElement("status")]
    public QueueTicketStatus Status { get; set; } = QueueTicketStatus.Waiting;

    [BsonElement("counterName")]
    public string? CounterName { get; set; }

    [BsonElement("checkedInAt")]
    public DateTime CheckedInAt { get; set; } = DateTime.UtcNow;

    [BsonElement("calledAt")]
    public DateTime? CalledAt { get; set; }

    /// <summary>Monotonic order of the last call today (from the <c>queue-call:{date}</c> counter).</summary>
    /// <remarks>WHY: two calls in the same millisecond share <see cref="CalledAt"/>; the 1:1 alternation needs an
    /// unambiguous "last called".</remarks>
    [BsonElement("callOrder")]
    [BsonIgnoreIfNull]
    public long? CallOrder { get; set; }

    [BsonElement("estimatedWaitMinutes")]
    public int EstimatedWaitMinutes { get; set; }

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }
}
