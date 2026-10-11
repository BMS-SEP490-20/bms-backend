namespace BMS.Core.Entities;

public enum UserRole
{
    Patient = 1,
    Receptionist = 2,
    Doctor = 3,
    LaboratoryUser = 4,
    Manager = 5,
    Administrator = 6
}

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public UserRole Role { get; set; } = UserRole.Patient;
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; } = false;
    public string? VerificationOtp { get; set; }
    public DateTime? OtpExpiresAt { get; set; }
    public string? PasswordResetOtp { get; set; }
    public DateTime? PasswordResetExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
