using System.ComponentModel.DataAnnotations;
using BMS.Core.Enums;

namespace BMS.Application.DTOs.Auth;

/// <summary>
/// Data Transfer Object for requesting OTP code generation and transmission (UC03).
/// Enforces strict validation according to OWASP Input Validation Guidelines.
/// </summary>
public class SendOtpRequestDto
{
    /// <summary>
    /// Recipient email address or telephone number.
    /// Must not be empty and must conform to valid telecom or email format.
    /// </summary>
    [Required(ErrorMessage = "Recipient destination cannot be empty.")]
    [StringLength(100, ErrorMessage = "Recipient destination cannot exceed 100 characters.")]
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Purpose of the authentication request. Default is PasswordReset.
    /// </summary>
    [Required(ErrorMessage = "Authentication purpose is required.")]
    public OtpPurpose Purpose { get; set; } = OtpPurpose.PasswordReset;

    /// <summary>
    /// Delivery channel (Email or Sms). Default is Email.
    /// </summary>
    public OtpChannel Channel { get; set; } = OtpChannel.Email;
}

/// <summary>
/// Data Transfer Object returned upon processing an OTP dispatch request.
/// </summary>
public class SendOtpResponseDto
{
    /// <summary>
    /// Indicates whether the dispatch request succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// User-friendly status message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Total validity lifetime of the generated OTP in seconds (default: 300s = 5 minutes).
    /// </summary>
    public int ExpiresInSeconds { get; set; } = 300;

    /// <summary>
    /// Minimum cooldown in seconds before the client is permitted to request a resend (anti-spam, default: 60s).
    /// </summary>
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Flag indicating whether the request was rejected due to sliding window Rate Limiting.
    /// </summary>
    public bool IsRateLimited { get; set; } = false;
}
