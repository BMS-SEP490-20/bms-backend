using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// System roles supported across the BMS platform.
/// </summary>
public enum UserRole
{
    Patient = 1,
    Receptionist = 2,
    Doctor = 3,
    LaboratoryUser = 4,
    Manager = 5,
    Administrator = 6
}

/// <summary>
/// Domain entity representing a clinical user or patient in the BMS platform.
/// Supports both in-memory store and MongoDB collection "users".
/// Conforms to: Production Engineering Standards & GDPR PII Security.
/// </summary>
public class User
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [BsonElement("fullName")]
    public string FullName { get; set; } = string.Empty;

    [BsonElement("patientCode")]
    public string? PatientCode { get; set; }

    [BsonElement("identityCardNumber")]
    public string? IdentityCardNumber { get; set; }

    [BsonElement("dateOfBirth")]
    public DateTime? DateOfBirth { get; set; }

    [BsonElement("gender")]
    public string? Gender { get; set; }

    [BsonElement("address")]
    public string? Address { get; set; }

    [BsonElement("bloodType")]
    public string? BloodType { get; set; }

    [BsonElement("role")]
    [BsonRepresentation(BsonType.String)]
    public UserRole Role { get; set; } = UserRole.Patient;

    [BsonElement("roleId")]
    public string? RoleId { get; set; }

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("passwordSalt")]
    public string PasswordSalt { get; set; } = string.Empty;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; } = false;

    [BsonElement("emailConfirmed")]
    public bool EmailConfirmed { get; set; } = false;

    [BsonElement("verificationOtp")]
    public string? VerificationOtp { get; set; }

    [BsonElement("otpExpiresAt")]
    public DateTime? OtpExpiresAt { get; set; }

    [BsonElement("passwordResetOtp")]
    public string? PasswordResetOtp { get; set; }

    [BsonElement("passwordResetExpiresAt")]
    public DateTime? PasswordResetExpiresAt { get; set; }

    [BsonElement("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
}
