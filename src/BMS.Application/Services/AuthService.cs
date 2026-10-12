using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BMS.Application.Services;

public sealed class AuthService(
    IUserRepository users,
    IRoleRepository roles,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    ITokenService tokens,
    IOptions<AuthOptions> authOptions,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly AuthOptions _auth = authOptions.Value;

    public async Task<AuthResponse> LoginAsync(LoginRequest request, ClientContext client, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var login = request.UsernameOrEmail.Trim();

        var user = await users.FindByLoginAsync(login, ct);
        if (user is null)
        {
            hasher.VerifyDummy(request.Password); // cân bằng thời gian phản hồi
            logger.LogWarning("Login failed: unknown account {Login} from {Ip}", login, client.IpAddress);
            throw new InvalidCredentialsException();
        }

        // Đang bị khóa -> không kiểm tra mật khẩu nữa (tránh bị brute-force trong thời gian khóa)
        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > now)
        {
            logger.LogWarning("Login blocked: user {UserId} is locked until {Until} (ip {Ip})",
                user.Id, lockoutEnd, client.IpAddress);
            throw new AccountLockedException(lockoutEnd - now);
        }

        if (!hasher.Verify(request.Password, user.Password))
        {
            var failed = await users.IncrementFailedLoginAsync(user.Id, ct);
            logger.LogWarning("Login failed: wrong password for user {UserId} ({Count}/{Max}) from {Ip}",
                user.Id, failed, _auth.MaxFailedAttempts, client.IpAddress);

            if (failed >= _auth.MaxFailedAttempts)
            {
                await users.LockAsync(user.Id, now.AddMinutes(_auth.LockoutMinutes), ct);
                logger.LogWarning("User {UserId} locked for {Minutes} minutes", user.Id, _auth.LockoutMinutes);
            }

            throw new InvalidCredentialsException();
        }

        // Chỉ báo "bị vô hiệu hóa" SAU khi mật khẩu đúng -> không lộ trạng thái cho người lạ
        if (!user.IsActive)
        {
            logger.LogWarning("Login rejected: user {UserId} is disabled (status {Status})", user.Id, user.Status);
            throw new AccountDisabledException();
        }

        var newHash = hasher.NeedsRehash(user.Password) ? hasher.Hash(request.Password) : null;
        await users.RecordSuccessfulLoginAsync(user.Id, now, newHash, ct);

        logger.LogInformation("Login success: user {UserId} from {Ip}", user.Id, client.IpAddress);

        var role = await roles.GetByIdAsync(user.RoleId, ct);
        return await IssueTokensAsync(user, role, Guid.NewGuid().ToString("N"), client, now, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, ClientContext client, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var stored = await refreshTokens.GetByHashAsync(tokens.HashToken(refreshToken), ct)
                     ?? throw new InvalidRefreshTokenException();

        // Token đã bị thu hồi mà vẫn được đem ra dùng => nghi bị đánh cắp: huỷ cả phiên
        if (stored.IsRevoked)
        {
            logger.LogWarning("Refresh token reuse detected for user {UserId}, family {Family}, ip {Ip}",
                stored.UserId, stored.FamilyId, client.IpAddress);
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, "reuse_detected", ct);
            throw new InvalidRefreshTokenException();
        }

        if (stored.ExpiresAt <= now)
            throw new InvalidRefreshTokenException();

        var user = await users.FindByIdAsync(stored.UserId, ct);
        if (user is null || !user.IsActive || (user.LockoutEnd is { } l && l > now))
        {
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, "user_not_allowed", ct);
            throw new InvalidRefreshTokenException();
        }

        var role = await roles.GetByIdAsync(user.RoleId, ct);

        // Rotate: thu hồi token cũ (atomic) rồi cấp token mới cùng family
        var next = tokens.GenerateRefreshToken();
        if (!await refreshTokens.TryRevokeAsync(stored.Id, now, "rotated", next.TokenHash, ct))
            throw new InvalidRefreshTokenException(); // request song song đã dùng token này trước

        return await IssueTokensAsync(user, role, stored.FamilyId, client, now, ct, next);
    }

    public async Task LogoutAsync(string userId, string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var stored = await refreshTokens.GetByHashAsync(tokens.HashToken(refreshToken), ct);
        // Chỉ cho phép thu hồi token của chính mình; idempotent, không báo lỗi nếu không tìm thấy
        if (stored is null || stored.UserId != userId) return;

        await refreshTokens.RevokeFamilyAsync(stored.FamilyId, clock.GetUtcNow().UtcDateTime, "logout", ct);
        logger.LogInformation("Logout: user {UserId}", userId);
    }

    public async Task LogoutAllAsync(string userId, CancellationToken ct = default)
    {
        await refreshTokens.RevokeAllForUserAsync(userId, clock.GetUtcNow().UtcDateTime, "logout_all", ct);
        logger.LogInformation("Logout all sessions: user {UserId}", userId);
    }

    public async Task<UserInfoDto> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null || !user.IsActive) throw new InvalidRefreshTokenException();

        var role = await roles.GetByIdAsync(user.RoleId, ct);
        return ToDto(user, role);
    }

    private async Task<AuthResponse> IssueTokensAsync(
        User user, Role? role, string familyId, ClientContext client, DateTime now,
        CancellationToken ct, RefreshTokenResult? prepared = null)
    {
        var access = tokens.CreateAccessToken(user, role);
        var refresh = prepared ?? tokens.GenerateRefreshToken();

        await refreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = refresh.ExpiresAt,
            CreatedByIp = client.IpAddress,
            UserAgent = client.UserAgent
        }, ct);

        return new AuthResponse(
            AccessToken: access.Token,
            TokenType: "Bearer",
            ExpiresIn: (int)Math.Max(0, (access.ExpiresAt - now).TotalSeconds),
            RefreshToken: refresh.RawToken,
            RefreshTokenExpiresAt: refresh.ExpiresAt,
            User: ToDto(user, role));
    }

    private static UserInfoDto ToDto(User u, Role? role) => new(
        u.Id, u.Username, u.Email, u.FullName, u.PhoneNumber, u.RoleId,
        role?.RoleName ?? "Unknown",
        role?.Permissions ?? new List<string>());
}
