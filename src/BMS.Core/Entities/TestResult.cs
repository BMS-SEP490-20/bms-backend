using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
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

/// <summary>Signature predicate shared by result editing and order cancellation (Quy trình Đợt 3A Q5).</summary>
public static class TestResultRules
{
    /// <summary>A result is signed once approved: it is then read-only and its order can no longer be cancelled.</summary>
    public static bool IsSigned(TestResult r) => r.ValidationStatus == ResultValidationStatus.DoctorApproved || r.ApprovedAt != null
        || !string.IsNullOrWhiteSpace(r.DigitalSignature) || string.Equals(r.Status, "APPROVED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Clinical evaluation flag for a test result measurement compared against biological reference intervals.
/// </summary>
public enum ResultFlag
{
    Normal = 0,
    Low = 1,
    High = 2,
    CriticalPanic = 3,
    Unknown = 4
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

    /// <summary>Original legacy flag, including codes requiring manual interpretation.</summary>
    [BsonElement("originalFlag")]
    [BsonIgnoreIfNull]
    public string? OriginalFlag { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// Represents a laboratory test result report sheet (UC24).
/// </summary>
public class TestResult : ISupportInitialize
{
    /// <summary>Preserves original backup fields while resolving legacy snake_case links and signatures.</summary>
    [BsonExtraElements]
    [JsonIgnore]
    public BsonDocument LegacyFields { get; set; } = new();

    [BsonElement("digitalSignature")]
    public string? DigitalSignature { get; set; }

    public void BeginInit() { }

    /// <summary>Normalizes legacy fields only when the canonical value was not present.</summary>
    public void EndInit()
    {
        string Text(string key) => LegacyFields.TryGetValue(key, out var v) && !v.IsBsonNull
            ? v.IsString ? v.AsString : v.ToString() ?? string.Empty : string.Empty;
        DateTime? Date(string key) => LegacyFields.TryGetValue(key, out var v) && v.IsBsonDateTime ? v.ToUniversalTime() : null;
        if (string.IsNullOrEmpty(SampleId)) SampleId = Text("sample_id");
        if (string.IsNullOrEmpty(PatientId)) PatientId = Text("patient_id");
        if (string.IsNullOrEmpty(AppointmentId)) AppointmentId = Text("appointment_id");
        if (string.IsNullOrEmpty(ResultCode)) ResultCode = Text("result_code");
        if (string.IsNullOrEmpty(PackageId)) PackageId = Text("package_id");
        if (string.IsNullOrEmpty(DeviceId)) DeviceId = Text("equipment_code");
        if (string.IsNullOrEmpty(EnteredByUserId)) EnteredByUserId = Text("technician_id");
        if (string.IsNullOrEmpty(ReviewedBy)) ReviewedBy = Text("reviewed_by");
        if (string.IsNullOrEmpty(ClinicalComment)) ClinicalComment = Text("conclusion_notes");
        if (string.IsNullOrWhiteSpace(DigitalSignature)) DigitalSignature = Text("digital_signature");
        ApprovedAt ??= Date("approved_at");
        if (CreatedAt == default) CreatedAt = Date("created_at") ?? default;
        UpdatedAt ??= Date("updated_at");
    }
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
    // Writers supply the timestamp explicitly; an absent historical date must remain detectable.
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Soft-delete flag (<c>isDeleted</c>); UC71 Sample Timeline and UC76 Test History already exclude
    /// documents where it is true. Replaces the former EF navigation properties (Appointment, BloodSample,
    /// TestParameters), which MongoDB cannot populate; parameters are stored in <see cref="Items"/>.
    /// </summary>
    [BsonElement("isDeleted")]
    [BsonIgnoreIfDefault]
    public bool IsDeleted { get; set; }
}
