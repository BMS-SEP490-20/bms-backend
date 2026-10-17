
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Trạng thái tài khoản trong hệ thống BMS.
/// </summary>
public enum UserStatus
{
    Active = 1
}

/// <summary>
/// Các vai trò người dùng trong hệ thống BMS.
/// </summary>
public enum UserRole
{
    Administrator = 1,
    Admin = 1,
    Doctor = 2,
    LaboratoryUser = 3,
    LabTechnician = 3,
    LabHead = 4,
    Nurse = 5,
    Manager = 5,
    Receptionist = 6,
    Cashier = 7,
    WarehouseStaff = 8,
    Patient = 9
}

/// <summary>
/// Domain entity đại diện cho tài khoản người dùng BMS.
/// Hỗ trợ thông tin bệnh nhân, xác thực, quên mật khẩu
/// và chống brute-force khi đăng nhập.
/// </summary>
[BsonIgnoreExtraElements]
public class User
{
    [BsonId]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [BsonElement("fullName")]
    public string FullName { get; set; } = string.Empty;

    // Thông tin bệnh nhân
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

    // Vai trò
    [BsonElement("roleId")]
    [BsonIgnoreIfNull]
    public int? RoleId { get; set; }

    [BsonIgnore]
    public UserRole Role
    {
        get => RoleId.HasValue ? (UserRole)RoleId.Value : UserRole.Patient;
        set => RoleId = (int)value;
    }

    // Xác thực tài khoản
    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("passwordSalt")]
    public string PasswordSalt { get; set; } = string.Empty;

    [BsonElement("status")]
    public int Status { get; set; } = (int)UserStatus.Active;

    [BsonElement("isActive")]
    public bool IsActive
    {
        get => Status == (int)UserStatus.Active;
        set => Status = value
            ? (int)UserStatus.Active
            : 0;
    }

    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; }

    [BsonElement("emailConfirmed")]
    public bool EmailConfirmed { get; set; }

    // OTP xác thực email
    [BsonElement("verificationOtp")]
    public string? VerificationOtp { get; set; }

    [BsonElement("otpExpiresAt")]
    public DateTime? OtpExpiresAt { get; set; }

    // OTP quên mật khẩu
    [BsonElement("passwordResetOtp")]
    public string? PasswordResetOtp { get; set; }

    [BsonElement("passwordResetExpiresAt")]
    public DateTime? PasswordResetExpiresAt { get; set; }

    // Chống brute-force
    [BsonElement("failedLoginCount")]
    public int FailedLoginCount { get; set; }

    [BsonElement("lockoutEnd")]
    public DateTime? LockoutEnd { get; set; }

    // Thông tin bổ sung
    [BsonElement("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }
}

