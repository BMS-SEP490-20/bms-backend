using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Status of a clinical test order in the laboratory information system.
/// </summary>
/// <remarks>
/// Main flow (BA:L38): AwaitingPayment → PaidPendingSample → SampleCollected → InAnalysis → Completed.
/// WHY new values are appended (7, 8) instead of renumbered: existing documents store the numeric value.
/// Never compare statuses with &lt; / &gt;; use <see cref="TestOrderStatusRules"/>.
/// </remarks>
public enum TestOrderStatus
{
    Draft = 1,
    /// <summary>Legacy "ordered" state (before Đợt 3); treated like AwaitingPayment.</summary>
    Ordered = 2,
    SampleCollected = 3,
    InAnalysis = 4,
    Completed = 5,
    Cancelled = 6,
    /// <summary>Created by the doctor, fee not yet paid; sampling is blocked unless STAT (payment deferred).</summary>
    AwaitingPayment = 7,
    /// <summary>Paid at the counter or by a verified VNPay callback; ready for sampling.</summary>
    PaidPendingSample = 8
}

/// <summary>Status predicates shared by ordering, payment and sampling.</summary>
public static class TestOrderStatusRules
{
    /// <summary>True once a specimen has been collected (cancel by the doctor is no longer possible).</summary>
    public static bool IsSampleCollectedOrLater(TestOrderStatus s) =>
        s is TestOrderStatus.SampleCollected or TestOrderStatus.InAnalysis or TestOrderStatus.Completed;

    /// <summary>Statuses before sample collection, from which a cancel is allowed.</summary>
    public static readonly TestOrderStatus[] BeforeCollection =
        [TestOrderStatus.Draft, TestOrderStatus.Ordered, TestOrderStatus.AwaitingPayment, TestOrderStatus.PaidPendingSample];

    /// <summary>Statuses from which the Manager may cancel as an exception (anything not finished).</summary>
    public static readonly TestOrderStatus[] ManagerCancellable =
        [.. BeforeCollection, TestOrderStatus.SampleCollected, TestOrderStatus.InAnalysis];

    /// <summary>Sampling gate (BA:L38): paid, or STAT with payment deferred (post-check).</summary>
    public static bool CanCollectSample(TestOrder o) =>
        o.Status == TestOrderStatus.PaidPendingSample
        || (o.PaymentDeferred && o.Status is TestOrderStatus.AwaitingPayment or TestOrderStatus.Ordered);
}

/// <summary>
/// Individual test or test package item within a test order requisition.
/// </summary>
[BsonIgnoreExtraElements]
public class TestOrderItem
{
    [BsonElement("test_code")]
    public string TestCode { get; set; } = string.Empty;

    [BsonElement("test_name")]
    public string TestName { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = "Biochemistry";

    [BsonElement("tube_type")]
    public string TubeType { get; set; } = "SST 4mL";

    /// <summary>Tube cap color key from <c>TubeRules</c> (e.g. Gray for glucose, Purple for CBC).</summary>
    [BsonElement("tube_color")]
    [BsonIgnoreIfNull]
    public string? TubeColor { get; set; }

    [BsonElement("volume_ml")]
    public double VolumeMl { get; set; } = 4.0;

    [BsonElement("turnaround_time")]
    public string TurnaroundTime { get; set; } = "~2h";

    [BsonElement("preparation_notes")]
    public string PreparationNotes { get; set; } = "None";

    [BsonElement("price")]
    public decimal Price { get; set; }
}

/// <summary>
/// Test Order entity representing a physician laboratory requisition (UC32 / Create Test Order).
/// Maps to the <c>test_orders</c> collection in MongoDB.
/// </summary>
[BsonIgnoreExtraElements]
public class TestOrder
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("order_number")]
    public string OrderNumber { get; set; } = string.Empty;

    [BsonElement("patient_id")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patient_name")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("patient_gender")]
    public string PatientGender { get; set; } = "Nam";

    [BsonElement("patient_age")]
    public int PatientAge { get; set; } = 30;

    [BsonElement("doctor_id")]
    public string DoctorId { get; set; } = string.Empty;

    [BsonElement("doctor_name")]
    public string DoctorName { get; set; } = string.Empty;

    [BsonElement("priority")]
    public SamplePriority Priority { get; set; } = SamplePriority.Normal;

    [BsonElement("status")]
    public TestOrderStatus Status { get; set; } = TestOrderStatus.Ordered;

    /// <summary>ICD-10 codes (≥1, format ^[A-Z][0-9]{2}(\.[0-9]{1,2})?$) — BA:L37, L81.</summary>
    [BsonElement("icd10_codes")]
    public List<string> Icd10Codes { get; set; } = new();

    /// <summary>True for STAT orders: sampling may start before payment, the fee is collected afterwards.</summary>
    [BsonElement("payment_deferred")]
    public bool PaymentDeferred { get; set; }

    [BsonElement("paid_at")]
    [BsonIgnoreIfNull]
    public DateTime? PaidAt { get; set; }

    [BsonElement("status_changed_by")]
    [BsonIgnoreIfNull]
    public string? StatusChangedBy { get; set; }

    [BsonElement("status_note")]
    [BsonIgnoreIfNull]
    public string? StatusNote { get; set; }

    [BsonElement("clinical_indication")]
    public string ClinicalIndication { get; set; } = string.Empty;

    [BsonElement("special_instructions")]
    public string? SpecialInstructions { get; set; }

    [BsonElement("scheduled_date")]
    public DateTime ScheduledDate { get; set; } = DateTime.UtcNow.Date;

    [BsonElement("total_amount")]
    public decimal TotalAmount { get; set; }

    [BsonElement("fasting_required")]
    public bool FastingRequired { get; set; }

    [BsonElement("fasting_hours")]
    public int FastingHours { get; set; }

    [BsonElement("items")]
    public List<TestOrderItem> Items { get; set; } = new();

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("created_by")]
    public string? CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
