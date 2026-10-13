namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for Short Message Service (SMS) delivery.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Dispatches an SMS text message containing the OTP code to a mobile number.
    /// </summary>
    /// <param name="phoneNumber">Recipient mobile telephone number.</param>
    /// <param name="otpCode">6-digit OTP code.</param>
    /// <param name="ct">CancellationToken.</param>
    Task<bool> SendOtpSmsAsync(string phoneNumber, string otpCode, CancellationToken ct = default);
}
