using BMS.Application.DTOs;

namespace BMS.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto);
    Task<AuthResponseDto> VerifyOtpAsync(VerifyOtpDto dto);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto dto);
    Task<AuthResponseDto> ChangePasswordAsync(string userId, ChangePasswordDto dto);
    Task<AuthResponseDto> ForgotPasswordAsync(ForgotPasswordDto dto);
    Task<AuthResponseDto> ResetPasswordAsync(ResetPasswordDto dto);
    Task<UserDto?> GetUserByIdAsync(string userId);
}
