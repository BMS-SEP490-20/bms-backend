namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for electronic mail delivery in the BMS platform.
/// Conforms to: Production Medical Standards & AGENTS-CODE.md.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Dispatches an HTML email containing a 6-digit OTP code formatted with Medical Blue aesthetics.
    /// </summary>
    Task<bool> SendOtpEmailAsync(
        string toEmail,
        string otpCode,
        string purposeDescription,
        int expiryMinutes = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Dispatches an HTML email confirming an appointment booking with details and fasting guidelines (UC22).
    /// </summary>
    Task<bool> SendAppointmentConfirmationEmailAsync(
        string toEmail,
        string patientName,
        string appointmentCode,
        string packageName,
        string appointmentDateTime,
        string location,
        string fastingNotes,
        CancellationToken ct = default);

    /// <summary>
    /// Dispatches a formatted HTML email notifying the patient of their laboratory test results (UC60 / BR-182).
    /// Automatically incorporates high-priority warning banners and critical parameters when Panic Values are detected.
    /// </summary>
    Task<bool> SendTestResultNotificationEmailAsync(
        string toEmail,
        string patientName,
        string testOrderId,
        string? testPackageName,
        string title,
        string message,
        bool isCriticalPanicValue,
        List<string>? criticalParameters,
        string? clinicalRecommendation,
        CancellationToken ct = default);
}
