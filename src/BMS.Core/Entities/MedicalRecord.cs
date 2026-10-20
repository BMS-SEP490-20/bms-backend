using MongoDB.Bson.Serialization.Attributes;
using System;

namespace BMS.Core.Entities;

/// <summary>
/// Clinical vital signs measured during patient consultation.
/// </summary>
public class VitalSigns
{
    [BsonElement("heartRate")]
    public int HeartRate { get; set; }

    [BsonElement("systolicBp")]
    public int SystolicBp { get; set; }

    [BsonElement("diastolicBp")]
    public int DiastolicBp { get; set; }

    [BsonElement("temperature")]
    public decimal Temperature { get; set; }

    [BsonElement("spo2")]
    public int Spo2 { get; set; }

    [BsonElement("heightCm")]
    public decimal HeightCm { get; set; }

    [BsonElement("weightKg")]
    public decimal WeightKg { get; set; }

    [BsonElement("bmi")]
    public decimal Bmi { get; set; }

    [BsonElement("bmiCategory")]
    public string BmiCategory { get; set; } = string.Empty;
}

/// <summary>
/// Electronic Medical Record (EMR) document conforming to Ministry of Health & WHO ICD-10 standards.
/// </summary>
[BsonIgnoreExtraElements]
public class MedicalRecord
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("recordCode")]
    public string RecordCode { get; set; } = string.Empty;

    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("doctorId")]
    public string DoctorId { get; set; } = string.Empty;

    [BsonElement("appointmentId")]
    public string? AppointmentId { get; set; }

    [BsonElement("chiefComplaint")]
    public string ChiefComplaint { get; set; } = string.Empty;

    [BsonElement("clinicalSymptoms")]
    public string? ClinicalSymptoms { get; set; }

    [BsonElement("vitals")]
    public VitalSigns Vitals { get; set; } = new();

    [BsonElement("icd10Code")]
    public string Icd10Code { get; set; } = string.Empty;

    [BsonElement("icd10Name")]
    public string? Icd10Name { get; set; }

    [BsonElement("diagnosis")]
    public string Diagnosis { get; set; } = string.Empty;

    [BsonElement("treatmentPlan")]
    public string? TreatmentPlan { get; set; }

    [BsonElement("prescriptionNotes")]
    public string? PrescriptionNotes { get; set; }

    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; } = false;

    [BsonElement("deletedAt")]
    public DateTime? DeletedAt { get; set; }

    [BsonElement("deletedBy")]
    public string? DeletedBy { get; set; }

    [BsonElement("deleteReason")]
    public string? DeleteReason { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
