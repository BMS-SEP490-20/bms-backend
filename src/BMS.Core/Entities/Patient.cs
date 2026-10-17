using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Biological gender of the patient for laboratory reference interval determination.
/// </summary>
public enum BiologicalGender
{
    Unspecified = 0,
    Male = 1,
    Female = 2
}

/// <summary>
/// Domain entity representing a clinical patient record (UC08).
/// Enforces PII data segregation, medical identification, and emergency contact details.
/// </summary>
public class Patient
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("userId")]
    public string? UserId { get; set; }

    [BsonElement("patientCode")]
    public string PatientCode { get; set; } = string.Empty;

    [BsonElement("fullName")]
    public string FullName { get; set; } = string.Empty;

    [BsonElement("dateOfBirth")]
    public DateTime? DateOfBirth { get; set; }

    [BsonElement("gender")]
    public BiologicalGender Gender { get; set; } = BiologicalGender.Unspecified;

    [BsonElement("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [BsonElement("email")]
    public string? Email { get; set; }

    [BsonElement("identityCardNumber")]
    public string? IdentityCardNumber { get; set; }

    [BsonElement("address")]
    public string? Address { get; set; }

    [BsonElement("bloodType")]
    public string? BloodType { get; set; }

    [BsonElement("emergencyContactName")]
    public string? EmergencyContactName { get; set; }

    [BsonElement("emergencyContactPhone")]
    public string? EmergencyContactPhone { get; set; }

    [BsonElement("medicalHistory")]
    public string? MedicalHistory { get; set; }

    [BsonElement("allergies")]
    public List<string> Allergies { get; set; } = [];

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
