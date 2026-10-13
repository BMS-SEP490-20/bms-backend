namespace BMS.Application.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public string SecretKey { get; set; } = default!;

    /// <summary>Access token sống ngắn (khuyến nghị 5–15 phút).</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh token sống dài, được xoay vòng (rotate) mỗi lần dùng.</summary>
    public int RefreshTokenDays { get; set; } = 7;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Số lần sai mật khẩu liên tiếp trước khi khóa tạm.</summary>
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}
