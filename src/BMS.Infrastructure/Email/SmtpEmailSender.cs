using System.Net;
using System.Net.Mail;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BMS.Infrastructure.Email;

/// <summary>Gửi mail qua SMTP (cấu hình trong section "Email").</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _o = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_o.Host))
            throw new InvalidOperationException("Chưa cấu hình Email:Host nên không thể gửi email.");

        using var message = new MailMessage
        {
            From = new MailAddress(_o.FromAddress, _o.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(to);

        using var client = new SmtpClient(_o.Host, _o.Port) { EnableSsl = _o.EnableSsl };
        if (!string.IsNullOrWhiteSpace(_o.Username))
            client.Credentials = new NetworkCredential(_o.Username, _o.Password);

        await client.SendMailAsync(message, ct);
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
