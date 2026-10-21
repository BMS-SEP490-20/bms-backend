using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Clinical Medical Assessment entity (UC50 / Add Medical Assessment - Iteration 3).
/// Captures doctor clinical consultation, patient vital signs, physical exam,
/// WHO ICD-10 diagnosis, risk severity, and encounter association.
/// Maps to MongoDB collection <c>medical_assessments</c>.
/// </summary>
[BsonIgnoreExtraElements]
public class MedicalAssessment
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("assessment_code")]
    public string AssessmentCode { get; set; } = string.Empty;

    [BsonElement("patient_id")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("patient_code")]
    [BsonIgnoreIfNull]
    public string? PatientCode { get; set; }

    [BsonElement("patient_name")]
    [BsonIgnoreIfNull]
    public string? PatientName { get; set; }

    [BsonElement("patient_dob")]
    [BsonIgnoreIfNull]
    public DateTime? PatientDob { get; set; }

    [BsonElement("patient_gender")]
    [BsonIgnoreIfNull]
    public string? PatientGender { get; set; }

    [BsonElement("encounter_id")]
    [BsonIgnoreIfNull]
    public string? EncounterId { get; set; }

    [BsonElement("encounter_type")]
    public string EncounterType { get; set; } = "ClinicVisit"; // ClinicVisit, FollowUp, Emergency, RoutineCheckup

    [BsonElement("encounter_date")]
    [BsonIgnoreIfNull]
    public DateTime? EncounterDate { get; set; }

    [BsonElement("doctor_id")]
    public string DoctorId { get; set; } = string.Empty;

    [BsonElement("doctor_name")]
    [BsonIgnoreIfNull]
    public string? DoctorName { get; set; }

    [BsonElement("department")]
    public string Department { get; set; } = "Khoa Khám Bệnh";

    [BsonElement("assessment_date")]
    public DateTime AssessmentDate { get; set; } = DateTime.UtcNow;

    [BsonElement("chief_complaint")]
    public string ChiefComplaint { get; set; } = string.Empty;

    [BsonElement("clinical_symptoms")]
    [BsonIgnoreIfNull]
    public string? ClinicalSymptoms { get; set; }

    [BsonElement("vitals")]
    public VitalSigns Vitals { get; set; } = new();

    [BsonElement("physical_examination")]
    [BsonIgnoreIfNull]
    public string? PhysicalExamination { get; set; }

    [BsonElement("icd10_code")]
    public string Icd10Code { get; set; } = string.Empty;

    [BsonElement("icd10_name")]
    [BsonIgnoreIfNull]
    public string? Icd10Name { get; set; }

    [BsonElement("preliminary_diagnosis")]
    [BsonIgnoreIfNull]
    public string? PreliminaryDiagnosis { get; set; }

    [BsonElement("final_diagnosis")]
    public string FinalDiagnosis { get; set; } = string.Empty;

    [BsonElement("severity")]
    public string Severity { get; set; } = "Mild"; // Mild, Moderate, Severe, Critical

    [BsonElement("risk_level")]
    public string RiskLevel { get; set; } = "Low"; // Low, Medium, High

    [BsonElement("clinical_impression")]
    [BsonIgnoreIfNull]
    public string? ClinicalImpression { get; set; }

    [BsonElement("recommended_test_packages")]
    public List<string> RecommendedTestPackages { get; set; } = new();

    [BsonElement("treatment_plan")]
    [BsonIgnoreIfNull]
    public string? TreatmentPlan { get; set; }

    [BsonElement("prescription_notes")]
    [BsonIgnoreIfNull]
    public string? PrescriptionNotes { get; set; }

    [BsonElement("follow_up_date")]
    [BsonIgnoreIfNull]
    public DateTime? FollowUpDate { get; set; }

    [BsonElement("follow_up_notes")]
    [BsonIgnoreIfNull]
    public string? FollowUpNotes { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "Draft"; // Draft, Finalized

    [BsonElement("is_finalized")]
    public bool IsFinalized { get; set; } = false;

    [BsonElement("finalized_at")]
    [BsonIgnoreIfNull]
    public DateTime? FinalizedAt { get; set; }

    [BsonElement("finalized_by")]
    [BsonIgnoreIfNull]
    public string? FinalizedBy { get; set; }

    [BsonElement("is_deleted")]
    public bool IsDeleted { get; set; } = false;

    [BsonElement("deleted_at")]
    [BsonIgnoreIfNull]
    public DateTime? DeletedAt { get; set; }

    [BsonElement("deleted_by")]
    [BsonIgnoreIfNull]
    public string? DeletedBy { get; set; }

    [BsonElement("delete_reason")]
    [BsonIgnoreIfNull]
    public string? DeleteReason { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("created_by")]
    [BsonIgnoreIfNull]
    public string? CreatedBy { get; set; }

    [BsonElement("updated_at")]
    [BsonIgnoreIfNull]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    [BsonIgnoreIfNull]
    public string? UpdatedBy { get; set; }
}
