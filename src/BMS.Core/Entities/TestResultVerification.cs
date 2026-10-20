using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Individual verification check item within the technical verification protocol (UC72).
/// </summary>
[BsonIgnoreExtraElements]
public class VerificationCheckItem
{
    [BsonElement("check_name")]
    public string CheckName { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "OK"; // "OK", "Flag", "None", "Confirmed"

    [BsonElement("status_type")]
    public string StatusType { get; set; } = "ok"; // "ok", "warn", "err"

    [BsonElement("details")]
    public string Details { get; set; } = string.Empty;
}

/// <summary>
/// Technical Laboratory Test Result Verification entity (UC72 / Verify Test Results).
/// Technical verification protocol executed by Lab Manager / Technical Supervisor before doctor approval.
/// Maps to MongoDB collection "test_verifications".
/// </summary>
[BsonIgnoreExtraElements]
public class TestResultVerification
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("specimen_id")]
    public string SpecimenId { get; set; } = string.Empty; // e.g. SPC-261006-A1-0089

    [BsonElement("order_id")]
    public string OrderId { get; set; } = string.Empty; // e.g. ORD-261006-0187

    [BsonElement("package_name")]
    public string PackageName { get; set; } = "Lipid Profile";

    [BsonElement("patient_id")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patient_name")]
    public string PatientName { get; set; } = string.Empty;

    [BsonElement("checklist")]
    public List<VerificationCheckItem> Checklist { get; set; } = new();

    [BsonElement("verifier_comment")]
    public string VerifierComment { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "PendingVerification"; // "PendingVerification" | "Verified" | "ReturnedForCorrection"

    [BsonElement("verified_by")]
    public string VerifiedBy { get; set; } = string.Empty;

    [BsonElement("verified_at")]
    public DateTime? VerifiedAt { get; set; }

    [BsonElement("returned_reason")]
    public string? ReturnedReason { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
