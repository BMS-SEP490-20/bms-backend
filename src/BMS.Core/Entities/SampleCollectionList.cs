using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Status of a sample collection list / phlebotomy run.
/// </summary>
public enum SampleCollectionListStatus
{
    Draft = 1,
    Confirmed = 2,
    InCollection = 3,
    Completed = 4,
    Cancelled = 5
}

/// <summary>
/// Individual specimen item within a sample collection list (UC37).
/// </summary>
[BsonIgnoreExtraElements]
public class SampleCollectionItem
{
    [BsonElement("specimen_id")]
    public string SpecimenId { get; set; } = string.Empty;

    [BsonElement("appointment_id")]
    public string AppointmentId { get; set; } = string.Empty;

    [BsonElement("patient_id")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patient_name")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("package_name")]
    public string PackageName { get; set; } = string.Empty;

    [BsonElement("tubes")]
    public List<string> Tubes { get; set; } = new(); // e.g. ["SST", "NaF"]

    [BsonElement("collector_id")]
    public string? CollectorId { get; set; }

    [BsonElement("collector_name")]
    public string CollectorName { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "PendingCollection";
}

/// <summary>
/// Entity representing a phlebotomy batch collection list (UC37 / Create Sample List).
/// Maps to the <c>sample_collection_lists</c> collection in MongoDB.
/// </summary>
[BsonIgnoreExtraElements]
public class SampleCollectionList
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("batch_code")]
    public string BatchCode { get; set; } = string.Empty;

    [BsonElement("location")]
    public string Location { get; set; } = string.Empty;

    [BsonElement("target_time")]
    public string TargetTime { get; set; } = string.Empty;

    [BsonElement("scheduled_date")]
    public DateTime ScheduledDate { get; set; } = DateTime.UtcNow.Date;

    [BsonElement("collector_id")]
    public string CollectorId { get; set; } = string.Empty;

    [BsonElement("collector_name")]
    public string CollectorName { get; set; } = string.Empty;

    [BsonElement("status")]
    public SampleCollectionListStatus Status { get; set; } = SampleCollectionListStatus.Confirmed;

    [BsonElement("items")]
    public List<SampleCollectionItem> Items { get; set; } = new();

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("created_by")]
    public string? CreatedBy { get; set; }
}
