using BMS.Application.DTOs;
using BMS.Application.DTOs.Auth;

namespace BMS.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, ClientContext client, CancellationToken ct = default);
    Task<AuthLoginResult> AuthenticateAsync(LoginRequest request, ClientContext client, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, ClientContext client, CancellationToken ct = default);
    Task LogoutAsync(string userId, string? refreshToken, CancellationToken ct = default);
    Task LogoutAllAsync(string userId, CancellationToken ct = default);
    Task<UserInfoDto> GetProfileAsync(string userId, CancellationToken ct = default);

    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto);
    Task<AuthResponseDto> VerifyOtpAsync(VerifyOtpDto dto);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto dto);
    Task<AuthResponseDto> ChangePasswordAsync(string userId, ChangePasswordDto dto);
    Task<AuthResponseDto> ForgotPasswordAsync(ForgotPasswordDto dto);
    Task<AuthResponseDto> ResetPasswordAsync(ResetPasswordDto dto);
    Task<UserDto?> GetUserByIdAsync(string userId);
}
