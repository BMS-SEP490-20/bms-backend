using BMS.Application.DTOs.Auth;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

public interface IPasswordResetTokenRepository
{
    Task AddAsync(PasswordResetToken token, CancellationToken ct = default);
    Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Đánh dấu đã dùng (atomic): chỉ thành công nếu token chưa dùng và chưa hết hạn.</summary>
    Task<bool> TryConsumeAsync(string id, DateTime now, CancellationToken ct = default);

    /// <summary>Vô hiệu hoá mọi token còn hiệu lực của user.</summary>
    Task InvalidateAllForUserAsync(string userId, DateTime now, CancellationToken ct = default);

    Task<DateTime?> GetLastCreatedAtAsync(string userId, CancellationToken ct = default);
}

public interface IPasswordResetService
{
    /// <summary>Luôn hoàn thành êm (không báo email có tồn tại hay không) để chống dò tài khoản.</summary>
    Task ForgotPasswordAsync(string email, ClientContext client, CancellationToken ct = default);

    /// <summary>Ném InvalidResetTokenException nếu token sai/đã dùng/hết hạn.</summary>
    Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default);
}
