using BMS.Application.DTOs.Users;

namespace BMS.Application.Interfaces;

/// <summary>
/// Application service contract for user profile query and management workflows (UC08).
/// Enforces business logic and PII masking rules.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Retrieves sanitized profile by authenticated user identifier (Prevents IDOR).
    /// </summary>
    Task<UserProfileResponseDto?> GetProfileByIdAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves sanitized profile by email or username identifier.
    /// </summary>
    Task<UserProfileResponseDto?> GetProfileByEmailOrUsernameAsync(string identifier, CancellationToken ct = default);

    /// <summary>
    /// Ensures a default verified user profile exists in repository for testing and demonstration.
    /// </summary>
    Task<UserProfileResponseDto> EnsureDefaultUserProfileAsync(CancellationToken ct = default);
}
