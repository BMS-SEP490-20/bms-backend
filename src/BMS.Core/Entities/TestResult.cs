using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Status of clinical test result verification.
/// </summary>
public enum ResultValidationStatus
{
    PendingEntry = 0,
    Entered = 1,
    TechnicianVerified = 2,
    DoctorApproved = 3,
    Rejected = 4
}

/// <summary>
/// Clinical evaluation flag for a test result measurement compared against biological reference intervals.
/// </summary>
public enum ResultFlag
{
    Normal = 0,
    Low = 1,
    High = 2,
    CriticalPanic = 3
}

/// <summary>
/// Individual test measurement record for a specific criterion/biomarker.
/// </summary>
public class TestResultItem
{
    [BsonElement("criterionId")]
    public string CriterionId { get; set; } = string.Empty;

    [BsonElement("criterionCode")]
    public string CriterionCode { get; set; } = string.Empty;

    [BsonElement("criterionName")]
    public string CriterionName { get; set; } = string.Empty;

    [BsonElement("measuredValue")]
    public string MeasuredValue { get; set; } = string.Empty;

    [BsonElement("unit")]
    public string Unit { get; set; } = string.Empty;

    [BsonElement("referenceInterval")]
    public string ReferenceInterval { get; set; } = string.Empty;

    [BsonElement("flag")]
    public ResultFlag Flag { get; set; } = ResultFlag.Normal;

    [BsonElement("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// Represents a laboratory test result report sheet (UC24).
/// </summary>
public class TestResult
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("testResultId")]
    public string TestResultId { get; set; } = string.Empty;

    [BsonElement("resultCode")]
    public string ResultCode { get; set; } = string.Empty;

    [BsonElement("appointmentId")]
    public string AppointmentId { get; set; } = string.Empty;

    [BsonElement("sampleId")]
    public string SampleId { get; set; } = string.Empty;

    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("packageId")]
    public string? PackageId { get; set; }

    [BsonElement("items")]
    public List<TestResultItem> Items { get; set; } = [];

    [BsonElement("validationStatus")]
    public ResultValidationStatus ValidationStatus { get; set; } = ResultValidationStatus.PendingEntry;

    [BsonElement("status")]
    public string? Status { get; set; }

    [BsonElement("enteredByUserId")]
    public string? EnteredByUserId { get; set; }

    [BsonElement("enteredAt")]
    public DateTime? EnteredAt { get; set; }

    [BsonElement("approvedByDoctorId")]
    public string? ApprovedByDoctorId { get; set; }

    [BsonElement("approvedAt")]
    public DateTime? ApprovedAt { get; set; }

    [BsonElement("reviewedBy")]
    public string? ReviewedBy { get; set; }

    [BsonElement("reviewedAt")]
    public DateTime? ReviewedAt { get; set; }

    [BsonElement("clinicalComment")]
    public string? ClinicalComment { get; set; }

    [BsonElement("isPanic")]
    public bool IsPanic { get; set; }

    [BsonElement("reagentCode")]
    public string? ReagentCode { get; set; }

    [BsonElement("quantity")]
    public decimal? Quantity { get; set; }

    [BsonElement("deviceId")]
    public string? DeviceId { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties for EF / In-Memory relational mapping (Ignored in MongoDB BSON serialization)
    [BsonIgnore]
    public virtual Appointment Appointment { get; set; } = null!;

    [BsonIgnore]
    public virtual BloodSample Sample { get; set; } = null!;

    [BsonIgnore]
    public virtual ICollection<TestParameter> TestParameters { get; set; } = new List<TestParameter>();
}