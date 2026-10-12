namespace BMS.Infrastructure.Configuration;

/// <summary>
/// Configuration options for SMTP email delivery in the BMS platform.
/// Conforms to: Production Engineering Standards.
/// </summary>
public class EmailSettings
{
    public const string SectionName = "EmailSettings";

    /// <summary>
    /// SMTP host server address (e.g., smtp.gmail.com).
    /// </summary>
    public string SmtpServer { get; set; } = "smtp.gmail.com";

    /// <summary>
    /// SMTP port number (typically 587 for TLS/STARTTLS or 465 for SSL).
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Authenticated sender email account.
    /// </summary>
    public string SenderEmail { get; set; } = "trantanphat2004lph@gmail.com";

    /// <summary>
    /// Display name shown in recipient's mail clients.
    /// </summary>
    public string SenderName { get; set; } = "BMS-Blood Laboratory and Clinic Management System";

    /// <summary>
    /// 16-character Google App Password (2FA App Password).
    /// </summary>
    public string AppPassword { get; set; } = string.Empty;

    /// <summary>
    /// Specifies whether to enforce SSL/TLS encryption.
    /// </summary>
    public bool EnableSsl { get; set; } = true;
}
