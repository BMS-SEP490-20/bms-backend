namespace BMS.Core.Entities;

/// <summary>
/// Refresh token (chỉ lưu SHA-256 hash, không lưu token gốc).
/// Các token cùng một phiên đăng nhập có chung FamilyId để thu hồi hàng loạt khi phát hiện bị đánh cắp.
/// </summary>
public class RefreshToken
{
    public string Id { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public string FamilyId { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsRevoked => RevokedAt is not null;
}
