using BMS.Application.DTOs;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Data;

namespace BMS.Application.Services;

public class AuthService : IAuthService
{
    private readonly BmsDbContext _context;

    public AuthService(BmsDbContext context)
    {
        _context = context;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, ClientContext client, CancellationToken ct = default)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u =>
            (u.Username.Equals(request.UsernameOrEmail, StringComparison.OrdinalIgnoreCase) ||
             u.Email.Equals(request.UsernameOrEmail, StringComparison.OrdinalIgnoreCase)) &&
            u.IsActive);

        if (user == null || !BmsDbContext.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        var accessToken = $"BMS_ACCESS_{user.Id}_{Guid.NewGuid():N}";
        var refreshToken = $"BMS_REFRESH_{user.Id}_{Guid.NewGuid():N}";
        var roleStr = user.Role.ToString();
        var roleId = (int)user.Role;
        var permissions = new List<string> { "all" };

        var userInfo = new UserInfoDto(
            user.Id,
            user.Username,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            roleId,
            roleStr,
            permissions
        );

        return new AuthResponse(
            accessToken,
            "Bearer",
            3600,
            refreshToken,
            DateTime.UtcNow.AddDays(7),
            userInfo
        );
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, ClientContext client, CancellationToken ct = default)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault() ?? new User
        {
            Id = "U-DEMO-001",
            Username = "demo_patient",
            Email = "demo@bms.vn",
            FullName = "Demo Patient",
            Role = UserRole.Patient
        };

        var newAccessToken = $"BMS_ACCESS_{user.Id}_{Guid.NewGuid():N}";
        var newRefreshToken = $"BMS_REFRESH_{user.Id}_{Guid.NewGuid():N}";
        var roleStr = user.Role.ToString();
        var roleId = (int)user.Role;
        var permissions = new List<string> { "all" };

        var userInfo = new UserInfoDto(
            user.Id,
            user.Username,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            roleId,
            roleStr,
            permissions
        );

        return new AuthResponse(
            newAccessToken,
            "Bearer",
            3600,
            newRefreshToken,
            DateTime.UtcNow.AddDays(7),
            userInfo
        );
    }

    public async Task LogoutAsync(string userId, string? refreshToken, CancellationToken ct = default)
    {
        await Task.CompletedTask;
    }

    public async Task LogoutAllAsync(string userId, CancellationToken ct = default)
    {
        await Task.CompletedTask;
    }

    public async Task<UserInfoDto> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            user = new User
            {
                Id = userId,
                Username = "bms_user",
                Email = "user@bms.vn",
                FullName = "BMS User",
                Role = UserRole.Patient
            };
        }

        var roleStr = user.Role.ToString();
        var roleId = (int)user.Role;
        var permissions = new List<string> { "all" };

        return new UserInfoDto(
            user.Id,
            user.Username,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            roleId,
            roleStr,
            permissions
        );
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto)
    {
        await Task.Yield();

        // 1. Check duplicate username or email
        var exists = _context.Users.Any(u => 
            u.Username.Equals(dto.Username, StringComparison.OrdinalIgnoreCase) || 
            u.Email.Equals(dto.Email, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Tên đăng nhập hoặc email đã được sử dụng trong hệ thống."
            };
        }

        // 2. Generate 6-digit OTP for email verification
        var otp = new Random().Next(100000, 999999).ToString();

        var newUser = new User
        {
            Username = dto.Username.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            FullName = dto.FullName.Trim(),
            PhoneNumber = dto.PhoneNumber,
            Address = dto.Address,
            DateOfBirth = dto.DateOfBirth,
            Gender = dto.Gender,
            Role = UserRole.Patient, // Default self-registered role
            PasswordHash = BmsDbContext.HashPassword(dto.Password),
            EmailConfirmed = false,
            VerificationOtp = otp,
            OtpExpiresAt = DateTime.UtcNow.AddMinutes(15),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(newUser);

        return new AuthResponseDto
        {
            Success = true,
            Message = $"Đăng ký tài khoản thành công! Mã OTP xác thực (6 chữ số) đã gửi tới email {dto.Email}. (Mã OTP demo: {otp})",
            User = MapToDto(newUser)
        };
    }

    public async Task<AuthResponseDto> VerifyOtpAsync(VerifyOtpDto dto)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u => u.Email.Equals(dto.Email, StringComparison.OrdinalIgnoreCase));
        if (user == null)
        {
            return new AuthResponseDto { Success = false, Message = "Tài khoản không tồn tại." };
        }

        if (user.VerificationOtp != dto.Otp || user.OtpExpiresAt < DateTime.UtcNow)
        {
            return new AuthResponseDto { Success = false, Message = "Mã OTP không đúng hoặc đã hết hạn (15 phút)." };
        }

        user.EmailConfirmed = true;
        user.VerificationOtp = null;
        user.OtpExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;

        return new AuthResponseDto
        {
            Success = true,
            Message = "Xác thực email thành công! Bạn có thể đăng nhập ngay.",
            User = MapToDto(user)
        };
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto dto)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u =>
            (u.Username.Equals(dto.UsernameOrEmail, StringComparison.OrdinalIgnoreCase) ||
             u.Email.Equals(dto.UsernameOrEmail, StringComparison.OrdinalIgnoreCase)) &&
            u.IsActive);

        if (user == null || !BmsDbContext.VerifyPassword(dto.Password, user.PasswordHash))
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Tên đăng nhập hoặc mật khẩu không chính xác."
            };
        }

        // Mock JWT token for client sessions
        var fakeJwtToken = $"BMS_TOKEN_{user.Id}_{user.Role}_{Guid.NewGuid():N}";

        return new AuthResponseDto
        {
            Success = true,
            Message = "Đăng nhập thành công.",
            Token = fakeJwtToken,
            User = MapToDto(user)
        };
    }

    public async Task<AuthResponseDto> ChangePasswordAsync(string userId, ChangePasswordDto dto)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return new AuthResponseDto { Success = false, Message = "Không tìm thấy người dùng." };
        }

        if (!BmsDbContext.VerifyPassword(dto.CurrentPassword, user.PasswordHash))
        {
            return new AuthResponseDto { Success = false, Message = "Mật khẩu hiện tại không chính xác." };
        }

        if (dto.NewPassword == dto.CurrentPassword)
        {
            return new AuthResponseDto { Success = false, Message = "Mật khẩu mới không được trùng với mật khẩu cũ." };
        }

        user.PasswordHash = BmsDbContext.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        return new AuthResponseDto
        {
            Success = true,
            Message = "Đổi mật khẩu thành công. Vui lòng đăng nhập lại với mật khẩu mới."
        };
    }

    public async Task<AuthResponseDto> ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u => u.Email.Equals(dto.Email, StringComparison.OrdinalIgnoreCase));
        if (user == null)
        {
            return new AuthResponseDto { Success = false, Message = "Email không tồn tại trong hệ thống." };
        }

        var otp = new Random().Next(100000, 999999).ToString();
        user.PasswordResetOtp = otp;
        user.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(15);

        return new AuthResponseDto
        {
            Success = true,
            Message = $"Mã OTP đặt lại mật khẩu đã được gửi đến email. (Mã OTP demo: {otp})"
        };
    }

    public async Task<AuthResponseDto> ResetPasswordAsync(ResetPasswordDto dto)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u => u.Email.Equals(dto.Email, StringComparison.OrdinalIgnoreCase));
        if (user == null)
        {
            return new AuthResponseDto { Success = false, Message = "Email không tồn tại." };
        }

        if (user.PasswordResetOtp != dto.Otp || user.PasswordResetExpiresAt < DateTime.UtcNow)
        {
            return new AuthResponseDto { Success = false, Message = "Mã OTP đặt lại mật khẩu không hợp lệ hoặc đã hết hạn." };
        }

        user.PasswordHash = BmsDbContext.HashPassword(dto.NewPassword);
        user.PasswordResetOtp = null;
        user.PasswordResetExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;

        return new AuthResponseDto
        {
            Success = true,
            Message = "Đặt lại mật khẩu thành công! Bạn có thể đăng nhập ngay."
        };
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u => u.Id == userId);
        return user != null ? MapToDto(user) : null;
    }

    private static UserDto MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Role = user.Role.ToString(),
            EmailConfirmed = user.EmailConfirmed
        };
    }
}
