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

    [BsonElement("resultCode")]
    public string ResultCode { get; set; } = string.Empty;

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

    [BsonElement("enteredByUserId")]
    public string? EnteredByUserId { get; set; }

    [BsonElement("enteredAt")]
    public DateTime? EnteredAt { get; set; }

    [BsonElement("approvedByDoctorId")]
    public string? ApprovedByDoctorId { get; set; }

    [BsonElement("approvedAt")]
    public DateTime? ApprovedAt { get; set; }

    [BsonElement("clinicalComment")]
    public string? ClinicalComment { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
