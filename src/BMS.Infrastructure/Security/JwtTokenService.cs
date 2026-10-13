using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using BMS.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BMS.Infrastructure.Security;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _jwt;
    private readonly TimeProvider _clock;
    private readonly SigningCredentials _signing;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
    {
        _jwt = options.Value;
        _clock = clock;
        _signing = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SecretKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessTokenResult CreateAccessToken(User user, Role? role)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("preferred_username", user.Username),
            new("name", user.FullName),
            new("role_id", user.RoleId.ToString()),
            new("role", role?.RoleName ?? "Unknown")
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
