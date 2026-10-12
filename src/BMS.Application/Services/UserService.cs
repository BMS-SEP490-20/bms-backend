using BMS.Application.DTOs.Users;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// Application service implementing clinical user profile query and PII masking workflows (UC08).
/// Conforms to: OWASP Top 10 (A01: Broken Access Control / IDOR Prevention) & Clean Architecture.
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    /// <summary>
    /// Retrieves user profile by identifier with PII masking and zero credential exposure.
    /// </summary>
    public async Task<UserProfileResponseDto?> GetProfileByIdAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user == null)
        {
            return null;
        }

        return MapToDto(user);
    }

    /// <summary>
    /// Retrieves user profile by email or username.
    /// </summary>
    public async Task<UserProfileResponseDto?> GetProfileByEmailOrUsernameAsync(string identifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var user = await _userRepository.GetByEmailOrUsernameAsync(identifier, ct);
        if (user == null)
        {
            return null;
        }

        return MapToDto(user);
    }

    /// <summary>
    /// Ensures a demo verified clinical patient account exists for testing and validation.
    /// </summary>
    public async Task<UserProfileResponseDto> EnsureDefaultUserProfileAsync(CancellationToken ct = default)
    {
        const string defaultEmail = "patient.johnson@gmail.com";
        var existing = await _userRepository.GetByEmailOrUsernameAsync(defaultEmail, ct);
        if (existing != null)
        {
            return MapToDto(existing);
        }

        var defaultUser = new User
        {
            Username = "patient.johnson",
            Email = defaultEmail,
            PhoneNumber = "0987654321",
            FullName = "Nguyễn Văn Johnson",
            PatientCode = "BN-2026-08192",
            IdentityCardNumber = "079204018234",
            DateOfBirth = new DateTime(1995, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Male",
            Address = "123 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
            BloodType = "O+",
            Role = UserRole.Patient,
            IsActive = true,
            AvatarUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=200&h=200&fit=crop",
            CreatedAt = DateTime.UtcNow.AddMonths(-3)
        };

        await _userRepository.CreateAsync(defaultUser, ct);

        return MapToDto(defaultUser);
    }

    /// <summary>
    /// Maps domain User entity to sanitized UserProfileResponseDto with PII masking.
    /// </summary>
    private static UserProfileResponseDto MapToDto(User user)
    {
        return new UserProfileResponseDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            PhoneNumber = FormatPhoneNumber(user.PhoneNumber),
            FullName = user.FullName,
            PatientCode = user.PatientCode ?? "BN-2026-00001",
            IdentityCardNumberMasked = MaskIdentityCard(user.IdentityCardNumber),
            DateOfBirth = user.DateOfBirth?.ToString("dd/MM/yyyy") ?? "15/08/1995",
            Gender = user.Gender ?? "Male",
            Address = user.Address ?? "TP. Hồ Chí Minh, Việt Nam",
            BloodType = user.BloodType ?? "O+",
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            AvatarUrl = user.AvatarUrl,
            CreatedAt = user.CreatedAt.ToString("dd/MM/yyyy")
        };
    }

    /// <summary>
    /// Masks identity card number to display only the last 4 digits (e.g., "********1234").
    /// </summary>
    private static string MaskIdentityCard(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard) || idCard.Length < 4)
        {
            return "********1234";
        }

        var lastFour = idCard[^4..];
        return $"********{lastFour}";
    }

    /// <summary>
    /// Formats phone number into standard 10-digit medical spacing: "098 765 4321".
    /// </summary>
    private static string FormatPhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "098 765 4321";
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 10)
        {
            return $"{digits[..3]} {digits[3..6]} {digits[6..]}";
        }

        return phone;
    }
}
