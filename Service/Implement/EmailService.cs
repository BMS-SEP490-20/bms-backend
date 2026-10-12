using iam_service.Service.Interface;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

public class EmailSettings
{
    public string? SmtpServer { get; set; }
    public int Port { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }
    public string? AppPassword { get; set; }
}

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;

    public EmailService(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        try
        {
            if (string.IsNullOrEmpty(toEmail))
                throw new ArgumentNullException(nameof(toEmail), "Địa chỉ người nhận không được để trống.");
            if (string.IsNullOrEmpty(_settings.SenderEmail))
                throw new InvalidOperationException("Thiếu cấu hình SenderEmail trong EmailSettings.");

            var client = new SmtpClient(_settings.SmtpServer, _settings.Port)
            {
                Credentials = new NetworkCredential(_settings.SenderEmail, _settings.AppPassword),
                EnableSsl = true
            };

            var mail = new MailMessage
            {
                From = new MailAddress(_settings.SenderEmail, _settings.SenderName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail);

            await client.SendMailAsync(mail);

            Console.WriteLine($"✅ [EmailService] Gửi mail thành công đến {toEmail}");
        }
        catch (SmtpException smtpEx)
        {
            Console.WriteLine($"❌ [SMTP ERROR] {smtpEx.StatusCode}: {smtpEx.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [EmailService ERROR] {ex.Message}");
            throw;
        }
    }
}