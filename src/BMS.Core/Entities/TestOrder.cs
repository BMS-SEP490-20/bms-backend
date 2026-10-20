using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Status of a clinical test order in the laboratory information system.
/// </summary>
public enum TestOrderStatus
{
    Draft = 1,
    Ordered = 2,
    SampleCollected = 3,
    InAnalysis = 4,
    Completed = 5,
    Cancelled = 6
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
