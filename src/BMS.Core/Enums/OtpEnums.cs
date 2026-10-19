using System.Text.Json.Serialization;

namespace BMS.Core.Enums;

/// <summary>
/// Business purpose of the One-Time Password (OTP) in the BMS platform.
/// Conforms to: NIST SP 800-63B & OWASP Authentication Guidelines.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OtpPurpose
{
    /// <summary>
    /// Verification for patient or staff account registration (UC02).
    /// </summary>
    Registration = 1,

    /// <summary>
    /// Verification for forgotten password recovery workflow (UC10).
    /// </summary>
    PasswordReset = 2,

    /// <summary>
    /// Verification for multi-factor authentication (2FA login).
    /// </summary>
    Login = 3,

    /// <summary>
    /// Verification for critical billing or sensitive clinical record modifications.
    /// </summary>
    Transaction = 4
}

/// <summary>
/// Telecommunication transmission channel for delivering the OTP code.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OtpChannel
{
    /// <summary>
    /// Dispatch code via Electronic Mail (SMTP / Cloud Mail Provider).
    /// </summary>
    Email = 1,

    /// <summary>
    /// Dispatch code via Short Message Service (SMS Brandname / Telecom Gateway).
    /// </summary>
    Sms = 2
}

