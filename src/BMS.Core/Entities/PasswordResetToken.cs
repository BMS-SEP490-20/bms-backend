namespace BMS.Core.Entities;

/// <summary>
/// Token đặt lại mật khẩu (chỉ lưu SHA-256 hash, token gốc chỉ gửi qua email).
/// Dùng 1 lần, hết hạn ngắn. Map với collection "password_reset_tokens".
/// </summary>
public class PasswordResetToken
{
    public string Id { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string? RequestedIp { get; set; }
}
