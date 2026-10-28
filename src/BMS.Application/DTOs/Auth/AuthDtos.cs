using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Auth;

public sealed class LoginRequest
{
    /// <summary>Username hoặc Email.</summary>
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập hoặc email.")]
    [StringLength(254, MinimumLength = 1)]
    public string UsernameOrEmail { get; init; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(128, MinimumLength = 1)] // giới hạn trên chống DoS khi hash (BCrypt chỉ dùng 72 byte đầu)
    public string Password { get; init; } = default!;
}

public sealed class RefreshTokenRequest
{
    [Required]
    [StringLength(512, MinimumLength = 20)]
    public string RefreshToken { get; init; } = default!;
}

public sealed class LogoutRequest
{
    [StringLength(512)]
    public string? RefreshToken { get; init; }
}

public sealed record UserInfoDto(
    string Id,
    string Username,
    string Email,
    string FullName,
    string? PhoneNumber,
    int RoleId,
    string Role,
    IReadOnlyList<string> Permissions);

public sealed record AuthResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserInfoDto User);

/// <summary>Thông tin client (phục vụ audit / phát hiện bất thường).</summary>
public sealed record ClientContext(string? IpAddress, string? UserAgent);

public sealed class AuthLoginResult
{
    public bool Success { get; init; }
    public AuthResponse? Response { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public int? RetryAfterMinutes { get; init; }

    public static AuthLoginResult Ok(AuthResponse response) =>
        new() { Success = true, Response = response, StatusCode = 200 };

    public static AuthLoginResult Fail(int statusCode, string message, string code, int? retryAfterMinutes = null) =>
        new()
        {
            Success = false,
            StatusCode = statusCode,
            ErrorMessage = message,
            ErrorCode = code,
            RetryAfterMinutes = retryAfterMinutes
        };
}
