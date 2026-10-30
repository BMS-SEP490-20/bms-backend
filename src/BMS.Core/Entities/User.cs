using System.ComponentModel;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Trạng thái tài khoản trong hệ thống BMS.
/// </summary>
public enum UserStatus
{
    Inactive = 0,
    Active = 1,
    Suspended = 2,
    Locked = 3
}

/// <summary>
/// Các vai trò người dùng trong hệ thống BMS. Giá trị số = <c>RoleId</c> đã seed trong DB
/// (quyết định 30/10/2026 điểm 1). Tên claim tương ứng nằm ở <see cref="BMS.Core.Constants.RoleNames"/>.
/// </summary>
public enum UserRole
{
    Admin = 1,
    Doctor = 2,
    Lab = 3,
    Receptionist = 4,
    Patient = 5,
    Manager = 6
}

/// <summary>
/// Domain entity đại diện cho tài khoản người dùng BMS.
/// Hỗ trợ tương thích song song cả lược đồ PascalCase (bms_master_database_setup.js)
/// và camelCase trong collection "accounts".
/// </summary>
[BsonIgnoreExtraElements]
public class User : ISupportInitialize
{
    private string _passwordHash = string.Empty;

    [BsonId]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("Username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("Email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("PhoneNumber")]
    public string? PhoneNumber { get; set; }

    [BsonElement("FullName")]
    public string FullName { get; set; } = string.Empty;

    // Thông tin bệnh nhân mở rộng
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
    [BsonElement("RoleId")]
    [BsonIgnoreIfNull]
    public int? RoleId { get; set; } = (int)UserRole.Patient;

    /// <summary>RoleId written by self-registration before Đợt 2 (AuthService used 9); read as Patient, never written again.</summary>
    public const int LegacyPatientRoleId = 9;

    [BsonIgnore]
    public UserRole Role
    {
        get {
        if (!RoleId.HasValue) return UserRole.Patient;
        // WHY: accounts self-registered before Đợt 2 were stored with RoleId 9; they are patients.
        // Safety net only: UserRoleMigration converts stored 9 → 6 at startup.
        // TODO(phat, DOT2-followup-5): remove the RoleId 9 branch once schema_migrations shows the migration ran in every environment.
        if (RoleId.Value == LegacyPatientRoleId) return UserRole.Patient;
        return Enum.IsDefined(typeof(UserRole), RoleId.Value) ? (UserRole)RoleId.Value : UserRole.Patient;
    }
        set => RoleId = (int)value;
    }

    // Xác thực tài khoản (đồng bộ giữa Password và PasswordHash)
    [BsonElement("Password")]
    public string Password
    {
        get => _passwordHash;
        set => _passwordHash = value ?? string.Empty;
    }

    [BsonElement("passwordHash")]
    public string PasswordHash
    {
        get => _passwordHash;
        set => _passwordHash = value ?? string.Empty;
    }

    [BsonElement("passwordSalt")]
    public string PasswordSalt { get; set; } = string.Empty;

    [BsonElement("Status")]
    public int Status { get; set; } = (int)UserStatus.Active;

    [BsonElement("isActive")]
    public bool IsActive
    {
        get => Status == (int)UserStatus.Active && !IsDeleted;
        set => Status = value
            ? (int)UserStatus.Active
            : (int)UserStatus.Inactive;
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
    [BsonElement("FailedLoginCount")]
    public int FailedLoginCount { get; set; }

    [BsonElement("LockoutEnd")]
    public DateTime? LockoutEnd { get; set; }

    // Thông tin bổ sung
    [BsonElement("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [BsonElement("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("LastLoginAt")]
    public DateTime? LastLoginAt { get; set; }

    [BsonExtraElements]
    public BsonDocument? ExtraElements { get; set; }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
        if (ExtraElements == null) return;

        if (string.IsNullOrWhiteSpace(Username) && ExtraElements.TryGetValue("username", out var u) && u.IsString)
            Username = u.AsString;

        if (string.IsNullOrWhiteSpace(Email) && ExtraElements.TryGetValue("email", out var e) && e.IsString)
            Email = e.AsString;

        if (string.IsNullOrWhiteSpace(FullName) && ExtraElements.TryGetValue("fullName", out var f) && f.IsString)
            FullName = f.AsString;

        if (string.IsNullOrWhiteSpace(PhoneNumber) && ExtraElements.TryGetValue("phoneNumber", out var p) && p.IsString)
            PhoneNumber = p.AsString;

        if (ExtraElements.TryGetValue("PasswordHash", out var phPascal) && phPascal.IsString &&
            !string.IsNullOrWhiteSpace(phPascal.AsString) &&
            (string.IsNullOrWhiteSpace(_passwordHash) || (_passwordHash.Length != 60 && phPascal.AsString.Length == 60)))
        {
            _passwordHash = phPascal.AsString;
        }
        else if (string.IsNullOrWhiteSpace(_passwordHash))
        {
            if (ExtraElements.TryGetValue("passwordHash", out var ph) && ph.IsString && !string.IsNullOrWhiteSpace(ph.AsString))
                _passwordHash = ph.AsString;
            else if (ExtraElements.TryGetValue("Password", out var pw) && pw.IsString && !string.IsNullOrWhiteSpace(pw.AsString))
                _passwordHash = pw.AsString;
        }

        if ((!RoleId.HasValue || RoleId.Value == 0) && ExtraElements.TryGetValue("roleId", out var r) && r.IsInt32)
            RoleId = r.AsInt32;

        if (ExtraElements.TryGetValue("status", out var s) && s.IsInt32)
            Status = s.AsInt32;

        if (FailedLoginCount == 0 && ExtraElements.TryGetValue("failedLoginCount", out var flc) && flc.IsInt32)
            FailedLoginCount = flc.AsInt32;

        if (LockoutEnd == null && ExtraElements.TryGetValue("lockoutEnd", out var le) && le.IsValidDateTime)
            LockoutEnd = le.ToUniversalTime();

        if (ExtraElements.TryGetValue("createdAt", out var ca) && ca.IsValidDateTime)
            CreatedAt = ca.ToUniversalTime();

        if (LastLoginAt == null && ExtraElements.TryGetValue("lastLoginAt", out var lla) && lla.IsValidDateTime)
            LastLoginAt = lla.ToUniversalTime();

        ExtraElements = null;
    }
}


