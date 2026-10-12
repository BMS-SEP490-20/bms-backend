using System.ComponentModel.DataAnnotations;
using BMS.Core.Enums;

namespace BMS.Application.DTOs.Auth;

/// <summary>
/// Data Transfer Object for validating an OTP code submitted by a client.
/// </summary>
public class VerifyOtpRequestDto
{
    /// <summary>
    /// Recipient email address or telephone number that received the OTP.
    /// </summary>
    [Required(ErrorMessage = "Recipient destination cannot be empty.")]
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// 6-digit numeric OTP code entered by the user.
    /// Strictly constrained to exactly 6 decimal digits.
    /// </summary>
    [Required(ErrorMessage = "OTP code cannot be empty.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP code must be exactly 6 numeric digits.")]
    public string Otp { get; set; } = string.Empty;

    /// <summary>
    /// Intended business purpose of the verification.
    /// </summary>
    [Required(ErrorMessage = "Authentication purpose is required.")]
    public OtpPurpose Purpose { get; set; } = OtpPurpose.PasswordReset;
}

/// <summary>
/// Data Transfer Object returned upon verifying an OTP code.
/// </summary>
public class VerifyOtpResponseDto
{
    /// <summary>
    /// Indicates whether verification succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Detailed result message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Cryptographically secure one-time verification token passed to subsequent steps
    /// (e.g. Password Reset or Account Activation) without re-submitting the OTP code.
    /// </summary>
    public string? VerificationToken { get; set; }

    /// <summary>
    /// Number of remaining allowed attempts before permanent invalidation.
    /// </summary>
    public int? RemainingAttempts { get; set; }
}
