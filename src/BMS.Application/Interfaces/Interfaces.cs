using BMS.Application.DTOs.Auth;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, ClientContext client, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, ClientContext client, CancellationToken ct = default);
    Task LogoutAsync(string userId, string? refreshToken, CancellationToken ct = default);
    Task LogoutAllAsync(string userId, CancellationToken ct = default);
    Task<UserInfoDto> GetProfileAsync(string userId, CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<User?> FindByLoginAsync(string usernameOrEmail, CancellationToken ct = default);
    Task<User?> FindByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Tăng đếm đăng nhập sai (atomic) và trả về giá trị mới.</summary>
    Task<int> IncrementFailedLoginAsync(string id, CancellationToken ct = default);
    Task LockAsync(string id, DateTime lockoutEnd, CancellationToken ct = default);

    /// <summary>Reset đếm sai, ghi LastLoginAt, (tuỳ chọn) nâng cấp hash.</summary>
    Task RecordSuccessfulLoginAsync(string id, DateTime now, string? newPasswordHash, CancellationToken ct = default);
}

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(int roleId, CancellationToken ct = default);
}

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
