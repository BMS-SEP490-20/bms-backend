using System.Net;
using System.Security.Cryptography;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BMS.Application.Services;

/// <summary>
/// Service orchestrating password recovery, reset token dispatch, and verification workflows (UC04, UC05).
/// Enforces security best practices:
/// - Generic responses to mitigate user enumeration.
/// - Cryptographic 256-bit token hashing (raw token never stored).
/// - Anti-spam request cooldowns.
/// </summary>
public sealed class PasswordResetService(
    IUserRepository users,
    IPasswordResetTokenRepository resetTokens,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    ITokenService tokens,
    IEmailSender email,
    IOptions<PasswordResetOptions> options,
    TimeProvider clock,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    private readonly PasswordResetOptions _opt = options.Value;

    public async Task ForgotPasswordAsync(string emailAddress, ClientContext client, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var user = await users.FindByEmailAsync(emailAddress.Trim(), ct);

        // Không tiết lộ email có tồn tại hay không: mọi nhánh đều kết thúc êm như nhau
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Forgot password: no active account for {Email} from {Ip}", emailAddress, client.IpAddress);
            return;
        }

        // Chống spam hộp thư của người dùng
        var last = await resetTokens.GetLastCreatedAtAsync(user.Id, ct);
        if (last is { } l && (now - l).TotalSeconds < _opt.CooldownSeconds)
        {
            logger.LogWarning("Forgot password: cooldown for user {UserId} from {Ip}", user.Id, client.IpAddress);
            return;
        }

        // Token ngẫu nhiên 256-bit; DB chỉ lưu hash
        var raw = GenerateRawToken();
        await resetTokens.InvalidateAllForUserAsync(user.Id, now, ct); // link cũ chết khi có link mới
        await resetTokens.AddAsync(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokens.HashToken(raw),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_opt.TokenMinutes),
            RequestedIp = client.IpAddress
        }, ct);

        var link = $"{_opt.ResetPasswordUrl}{(_opt.ResetPasswordUrl.Contains('?') ? '&' : '?')}token={Uri.EscapeDataString(raw)}";
        var to = user.Email;
        var body = BuildEmailBody(user.FullName, link, _opt.TokenMinutes);

        // Gửi nền: thời gian phản hồi không phụ thuộc việc tài khoản có tồn tại hay không
        _ = Task.Run(async () =>
        {
            try
            {
                await email.SendAsync(to, "Đặt lại mật khẩu BMS", body);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send password reset email to user {UserId}", user.Id);
            }
        }, CancellationToken.None);

        logger.LogInformation("Password reset requested: user {UserId} from {Ip}", user.Id, client.IpAddress);
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var stored = await resetTokens.GetByHashAsync(tokens.HashToken(token), ct);
        if (stored is null || stored.UsedAt is not null || stored.ExpiresAt <= now)
            throw new InvalidResetTokenException();

        var user = await users.FindByIdAsync(stored.UserId, ct);
        if (user is null || !user.IsActive)
            throw new InvalidResetTokenException();

        // Tiêu thụ token trước (atomic) -> 2 request song song chỉ 1 request thành công
        if (!await resetTokens.TryConsumeAsync(stored.Id, now, ct))
            throw new InvalidResetTokenException();

        await users.UpdatePasswordAsync(user.Id, hasher.Hash(newPassword), ct);
        await resetTokens.InvalidateAllForUserAsync(user.Id, now, ct);

        // Đổi mật khẩu => mọi phiên đăng nhập cũ (có thể của kẻ xấu) bị thu hồi
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, "password_reset", ct);

        logger.LogInformation("Password reset completed: user {UserId}", user.Id);
    }

    private static string GenerateRawToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string BuildEmailBody(string fullName, string link, int minutes)
    {
        var name = WebUtility.HtmlEncode(fullName);
        var href = WebUtility.HtmlEncode(link);
        return $"""
            <div style="font-family:Arial,sans-serif;font-size:14px;color:#222">
              <p>Xin chào {name},</p>
              <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản BMS của bạn.</p>
              <p><a href="{href}" style="display:inline-block;padding:10px 18px;background:#0d6efd;color:#fff;text-decoration:none;border-radius:6px">Đặt lại mật khẩu</a></p>
              <p>Liên kết có hiệu lực trong {minutes} phút và chỉ dùng được một lần.</p>
              <p>Nếu bạn không yêu cầu, hãy bỏ qua email này – mật khẩu của bạn vẫn an toàn.</p>
            </div>
            """;
    }
}
