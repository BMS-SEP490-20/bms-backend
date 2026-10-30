using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BMS.Infrastructure.Security;

/// <summary>
/// Service generating cryptographic JSON Web Tokens (JWT) and refresh tokens.
/// Conforms to: OWASP Top 10 &amp; AGENTS-CODE.md.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _jwt;
    private readonly TimeProvider _clock;
    private readonly SigningCredentials _signing;
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>
    /// Initializes a new instance of <see cref="JwtTokenService"/>.
    /// </summary>
    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
    {
        _jwt = options.Value;
        _clock = clock;
        _signing = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SecretKey)),
            SecurityAlgorithms.HmacSha256);
    }

    /// <summary>
    /// Generates a signed JWT access token containing standard identity claims and permissions.
    /// </summary>
    public AccessTokenResult CreateAccessToken(User user, Role? role)
    {
        // WHY (R8): the role claim must be one of the six canonical names, derived from the seeded RoleId,
        // never from a display name in the roles collection. No resolvable role => no token.
        // TODO(phat, DOT2-followup-5): drop the RoleId 9 safety net after UserRoleMigration has run everywhere.
        var roleId = user.RoleId == User.LegacyPatientRoleId ? (int)UserRole.Patient : user.RoleId ?? role?.Id;
        var roleName = RoleNames.FromRoleId(roleId) ?? RoleNames.Normalize(role?.RoleName)
            ?? throw new RoleNotAssignedException();
        roleId = RoleNames.ToRoleId(roleName);

        var now = _clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("preferred_username", user.Username),
            new("name", user.FullName),
            new("role_id", roleId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("role", roleName)
        };

        // Mỗi permission là 1 claim; JWT sẽ serialize thành mảng
        foreach (var permission in role?.Permissions ?? [])
            claims.Add(new Claim("permission", permission));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            IssuedAt = now,
            Expires = expires,
            SigningCredentials = _signing
        });

        return new AccessTokenResult(token, expires);
    }

    public RefreshTokenResult GenerateRefreshToken()
    {
        // 64 byte từ CSPRNG; token gốc chỉ trả cho client, DB chỉ lưu hash
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        var expires = _clock.GetUtcNow().UtcDateTime.AddDays(_jwt.RefreshTokenDays);
        return new RefreshTokenResult(raw, HashToken(raw), expires);
    }

    public string HashToken(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
