namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for electronic mail delivery in the BMS platform.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Dispatches an HTML email containing a 6-digit OTP code formatted with Medical Blue aesthetics.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="otpCode">Raw 6-digit OTP code (transmitted only over TLS).</param>
    /// <param name="purposeDescription">Human-readable purpose description.</param>
    /// <param name="expiryMinutes">Validity duration in minutes (default: 5).</param>
    /// <param name="ct">CancellationToken.</param>
    Task<bool> SendOtpEmailAsync(
        string toEmail,
        string otpCode,
        string purposeDescription,
        int expiryMinutes = 5,
        CancellationToken ct = default);
}
