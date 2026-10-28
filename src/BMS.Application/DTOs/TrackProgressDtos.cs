using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs;

/// <summary>
/// Status of a milestone step in the sample progress lifecycle.
/// </summary>
public enum TrackingStepStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Warning = 3,
    Rejected = 4
}

/// <summary>
/// Details of an individual lifecycle step in the 6-stage clinical laboratory process.
/// </summary>
public class TrackingStepDto
{
    public int StepNumber { get; set; }
    public string StepCode { get; set; } = string.Empty; // REGISTERED, SAMPLE_COLLECTED, LAB_RECEIVED, ANALYZING, MEDICAL_REVIEW, COMPLETED
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TrackingStepStatus Status { get; set; } = TrackingStepStatus.Pending;
    public string StatusText { get; set; } = "Chờ xử lý";
    public DateTime? Timestamp { get; set; }
    public string? HandlerName { get; set; }
    public string? Department { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Complete tracking progress response conforming to ISO 15189 and patient privacy standards.
/// </summary>
public class TrackProgressResponse
{
    public string OrderNumber { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientGender { get; set; }
    public int? PatientAge { get; set; }
    public string Priority { get; set; } = "Normal"; // Normal, Urgent, Stat
    public List<string> TestPackageNames { get; set; } = new();
    public string OverallStatus { get; set; } = "Pending";
    public int CurrentStepNumber { get; set; } = 1;
    public string CurrentStepName { get; set; } = string.Empty;
    public DateTime OrderedAt { get; set; }
    public DateTime EstimatedCompletionTime { get; set; }
    public double ElapsedMinutes { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsRejected { get; set; }
    public string? RejectionReason { get; set; }
    public bool IsRerunRequested { get; set; }
    public string? RerunReason { get; set; }
    public List<TrackingStepDto> Steps { get; set; } = new();
}
