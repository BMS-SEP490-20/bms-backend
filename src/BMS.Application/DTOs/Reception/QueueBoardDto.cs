using BMS.Core.Entities;
using System.Collections.Generic;

namespace BMS.Application.DTOs.Reception;

public class QueueTicketDto
{
    public string Id { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public bool IsWalkIn { get; set; }
    public string CheckedInTime { get; set; } = string.Empty;
    public int WaitTimeMinutes { get; set; }
    public string Priority { get; set; } = "Routine";
    public string Status { get; set; } = "Waiting";
    public string? CounterName { get; set; }
}

public class QueueStatsDto
{
    public int InQueueNow { get; set; } = 7;
    public int AvgWaitMinutes { get; set; } = 12;
    public int LongestWaitMinutes { get; set; } = 16;
    public int ServedToday { get; set; } = 18;
    public string CountersActive { get; set; } = "2 / 3";
}

public class CounterStatusDto
{
    public string CounterName { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public string Status { get; set; } = "Active"; // Active, Available, Closed
    public string Description { get; set; } = string.Empty;
}

public class QueueBoardDto
{
    public List<QueueTicketDto> CurrentQueue { get; set; } = new();
    public string NextNumber { get; set; } = string.Empty;
    public QueueStatsDto Stats { get; set; } = new();
    public List<CounterStatusDto> Counters { get; set; } = new();
}

public class IssueTicketRequestDto
{
    public string? PatientId { get; set; }
    public string? PatientName { get; set; }
    public string? PatientCode { get; set; }
    public string? PackageName { get; set; }
    public bool IsWalkIn { get; set; } = true;
    public QueuePriority Priority { get; set; } = QueuePriority.Routine;
}

public class CallTicketRequestDto
{
    public string CounterName { get; set; } = string.Empty;
}

public class UpdateTicketStatusRequestDto
{
    public QueueTicketStatus Status { get; set; }
    public string? CounterName { get; set; }
}
