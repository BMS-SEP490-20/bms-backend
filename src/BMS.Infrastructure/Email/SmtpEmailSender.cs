using System;
using System.Net;
using System.Net.Mail;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BMS.Infrastructure.Email;

/// <summary>Gửi mail qua SMTP (hỗ trợ Gmail SMTP, Outlook, Custom SMTP). Tự động chuẩn hóa App Password và FromAddress.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender>? logger = null) : IEmailSender
{
    private readonly EmailOptions _o = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var username = (_o.Username ?? string.Empty).Trim();
        var password = (_o.Password ?? string.Empty).Replace(" ", "").Trim();

        var fromAddress = !string.IsNullOrWhiteSpace(_o.FromAddress) ? _o.FromAddress.Trim() : username;
        if ((string.IsNullOrWhiteSpace(fromAddress) || fromAddress.EndsWith("@bms.com", StringComparison.OrdinalIgnoreCase)) &&
            _o.Host?.Contains("gmail.com", StringComparison.OrdinalIgnoreCase) == true &&
            !string.IsNullOrWhiteSpace(username))
        {
            fromAddress = username;
        }

        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            fromAddress = !string.IsNullOrWhiteSpace(username) ? username : "no-reply@bms.com";
        }

        var fromName = !string.IsNullOrWhiteSpace(_o.FromName) ? _o.FromName.Trim() : "BMS Laboratory";

        if (string.IsNullOrWhiteSpace(_o.Host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger?.LogWarning("[BMS Email Service] Chưa cấu hình đầy đủ SMTP Host/Username/Password. Thực hiện gửi giả lập (Dev mode) tới {To} | Subject: {Subject}\nBody:\n{Body}", to, subject, htmlBody);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(to);

        using var client = new SmtpClient(_o.Host, _o.Port)
        {
            EnableSsl = _o.EnableSsl,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        try
        {
            await client.SendMailAsync(message, ct);
            logger?.LogInformation("[BMS Email Service] Đã gửi email thành công tới {To} qua máy chủ {Host}:{Port} (From: {From})", to, _o.Host, _o.Port, fromAddress);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[BMS Email Service] Không thể gửi email qua máy chủ SMTP {Host}:{Port} (User: {User}). Lỗi: {Message}", _o.Host, _o.Port, username, ex.Message);
        }
    }
}

/// <summary>CHỈ dùng khi Development và chưa cấu hình SMTP: in nội dung mail (có link đặt lại) ra log.</summary>
public sealed class DevLoggingEmailSender(ILogger<DevLoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogWarning("[DEV EMAIL - chưa cấu hình SMTP] To: {To} | Subject: {Subject}\n{Body}", to, subject, htmlBody);
        return Task.CompletedTask;
    }
}
