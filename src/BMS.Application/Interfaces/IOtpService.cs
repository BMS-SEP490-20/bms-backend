using BMS.Application.DTOs.Auth;

namespace BMS.Application.Interfaces;

/// <summary>
/// Business service interface orchestrating OTP generation, rate limiting, and verification (UC03).
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Processes an OTP request, verifies rate limits, generates CSPRNG code, hashes, and dispatches via selected channel.
    /// </summary>
    /// <param name="request">OTP generation request parameters.</param>
    /// <param name="clientIp">Originating IP address for rate limit accounting.</param>
    /// <param name="ct">CancellationToken.</param>
    Task<SendOtpResponseDto> SendOtpAsync(SendOtpRequestDto request, string clientIp, CancellationToken ct = default);

    /// <summary>
    /// Verifies a submitted OTP against the salted hash persisted in the data store using constant-time comparison.
    /// </summary>
    /// <param name="request">OTP verification parameters.</param>
    /// <param name="ct">CancellationToken.</param>
    Task<VerifyOtpResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request, CancellationToken ct = default);
}
