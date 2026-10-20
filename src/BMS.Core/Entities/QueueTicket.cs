using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Clinical priority of patient in queue (Routine, Urgent, STAT emergency).
/// </summary>
public enum QueuePriority
{
    Routine = 1,
    Urgent = 2,
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
    public string TicketNumber { get; set; } = string.Empty; // e.g. Q-012, Q-019

    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patientName")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("patientCode")]
    public string PatientCode { get; set; } = string.Empty; // e.g. PT-012056

    [BsonElement("packageName")]
    public string PackageName { get; set; } = string.Empty;

    [BsonElement("isWalkIn")]
    public bool IsWalkIn { get; set; } = false;

    [BsonElement("priority")]
    public QueuePriority Priority { get; set; } = QueuePriority.Routine;

    [BsonElement("status")]
    public QueueTicketStatus Status { get; set; } = QueueTicketStatus.Waiting;

    [BsonElement("counterName")]
    public string? CounterName { get; set; } // e.g. Counter 1, Counter 2

    [BsonElement("checkedInAt")]
    public DateTime CheckedInAt { get; set; } = DateTime.UtcNow;

    [BsonElement("calledAt")]
    public DateTime? CalledAt { get; set; }

    [BsonElement("estimatedWaitMinutes")]
    public int EstimatedWaitMinutes { get; set; } = 10;
}
