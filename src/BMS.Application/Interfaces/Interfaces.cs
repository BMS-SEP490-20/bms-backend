using BMS.Application.DTOs.Auth;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;



public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Thu hồi atomic: chỉ thành công nếu token chưa bị thu hồi (chống race khi refresh song song).</summary>
    Task<bool> TryRevokeAsync(string id, DateTime now, string reason, string? replacedByHash, CancellationToken ct = default);
    Task RevokeFamilyAsync(string familyId, DateTime now, string reason, CancellationToken ct = default);
    Task RevokeAllForUserAsync(string userId, DateTime now, string reason, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
    bool NeedsRehash(string hash);

    /// <summary>Chạy 1 lần verify giả để thời gian phản hồi không lộ việc tài khoản có tồn tại hay không.</summary>
    void VerifyDummy(string password);
}

public sealed record AccessTokenResult(string Token, DateTime ExpiresAt);
public sealed record RefreshTokenResult(string RawToken, string TokenHash, DateTime ExpiresAt);

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(User user, Role? role);
    RefreshTokenResult GenerateRefreshToken();
    string HashToken(string rawToken);
}
