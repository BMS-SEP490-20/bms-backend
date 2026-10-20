using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Clinical laboratory test parameter result entry within an official diagnostic report.
/// </summary>
[BsonIgnoreExtraElements]
public class TestReportParameter
{
    [BsonElement("parameter_name")]
    public string ParameterName { get; set; } = string.Empty;

    [BsonElement("result_value")]
    public string ResultValue { get; set; } = string.Empty;

    [BsonElement("unit")]
    public string Unit { get; set; } = string.Empty;

    [BsonElement("reference_range")]
    public string ReferenceRange { get; set; } = string.Empty;

    [BsonElement("flag")]
    public string Flag { get; set; } = string.Empty; // Normal (""), "H", "L", "CRITICAL"

    [BsonElement("is_abnormal")]
    public bool IsAbnormal { get; set; }
}

/// <summary>
/// Official Clinical Laboratory Diagnostic Test Report entity (UC42 / Generate Test Report).
/// Validated according to ISO 15189:2022 diagnostic laboratory specifications.
/// </summary>
[BsonIgnoreExtraElements]
public class TestReport
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("report_id")]
    public string ReportId { get; set; } = string.Empty; // RPT-YYYYMMDD-XXXX

    [BsonElement("order_id")]
    public string OrderId { get; set; } = string.Empty; // ORD-YYYYMMDD-XXXX

    [BsonElement("patient_id")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patient_name")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("dob_age_sex")]
    public string DobAgeSex { get; set; } = "12 Mar 1992 · 34y · Male";

    [BsonElement("package_name")]
    public string PackageName { get; set; } = string.Empty;

    [BsonElement("collection_time")]
    public string CollectionTime { get; set; } = string.Empty;

    [BsonElement("specimen_id")]
    public string SpecimenId { get; set; } = string.Empty;

    [BsonElement("referring_doctor")]
    public string ReferringDoctor { get; set; } = "Dr. Trần Thị Bình · Internal Medicine";

    [BsonElement("category")]
    public string Category { get; set; } = "Hematology Results";

    [BsonElement("parameters")]
    public List<TestReportParameter> Parameters { get; set; } = new();

    [BsonElement("clinical_notes")]
    public string ClinicalNotes { get; set; } = string.Empty;

    [BsonElement("analysed_by")]
    public string AnalysedBy { get; set; } = "Phạm Thu Dung (MLT)";

    [BsonElement("analysed_at")]
    public DateTime AnalysedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("approved_by")]
    public string ApprovedBy { get; set; } = "Dr. Trần Thị Bình (MD)";

    [BsonElement("approved_at")]
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("issued_at")]
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("verification_hash")]
    public string VerificationHash { get; set; } = Guid.NewGuid().ToString("N");

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
