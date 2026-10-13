namespace BMS.Application.Options;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>Trang đặt lại mật khẩu của frontend; token được nối vào query (?token=...).</summary>
    public string ResetPasswordUrl { get; set; } = "http://localhost:5175/reset-password";

    /// <summary>Thời hạn token (phút).</summary>
    public int TokenMinutes { get; set; } = 30;

    /// <summary>Khoảng cách tối thiểu giữa 2 lần gửi mail cho cùng 1 tài khoản (giây) – chống spam hộp thư.</summary>
    public int CooldownSeconds { get; set; } = 60;
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@bms.local";
    public string FromName { get; set; } = "BMS";
}
