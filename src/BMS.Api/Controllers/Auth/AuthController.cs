using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Auth;

/// <summary>
/// API Controller managing identity verification and One-Time Password workflows (UC03).
/// Handles cryptographic code issuance and multi-factor authentication (2FA) verification.
/// Conforms to: RESTful API Design, OWASP ASVS & RFC 7807 Problem Details.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/auth")]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IOtpService _otpService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IOtpService otpService, ILogger<AuthController> logger)
    {
        _otpService = otpService;
        _logger = logger;
    }

    /// <summary>
    /// Generates and dispatches a 6-digit OTP code via Email or SMS for Registration or Password Recovery (I1-011).
    /// </summary>
    /// <param name="request">Recipient destination and business purpose.</param>
    /// <param name="ct">CancellationToken.</param>
    /// <response code="200">OTP code dispatched successfully with validity duration and cooldown window.</response>
    /// <response code="400">Invalid request payload or malformed destination.</response>
    /// <response code="429">Rate limit threshold exceeded (limit: 3 requests per 5 minutes).</response>
    [HttpPost("otp/send")]
    [ProducesResponseType(typeof(SendOtpResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SendOtpResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SendOtpResponseDto), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendOtp(
        [FromBody] SendOtpRequestDto request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new SendOtpResponseDto
            {
                Success = false,
                Message = "Invalid OTP dispatch request payload."
            });
        }

        // Extract client IP address to protect against automated SMS flooding and brute force
        var clientIp = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? "Unknown-IP";

        _logger.LogInformation(
            "[BMS Auth API] Processing OTP dispatch to {Destination} (Purpose: {Purpose}) from IP {ClientIp}",
            request.Destination,
            request.Purpose,
            clientIp);

        var result = await _otpService.SendOtpAsync(request, clientIp, ct);

        // Handle Rate Limit rejections
        if (result.IsRateLimited)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, result);
        }

        // Handle operational or carrier delivery rejections
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Verifies a 6-digit OTP code submitted by the user.
    /// </summary>
    /// <param name="request">Recipient destination, OTP code, and purpose.</param>
    /// <param name="ct">CancellationToken.</param>
    /// <response code="200">OTP verified successfully; returns one-time Verification Token.</response>
    /// <response code="400">Incorrect OTP, expired code, or locked after 5 failed attempts.</response>
    [HttpPost("otp/verify")]
    [ProducesResponseType(typeof(VerifyOtpResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(VerifyOtpResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyOtp(
        [FromBody] VerifyOtpRequestDto request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new VerifyOtpResponseDto
            {
                Success = false,
                Message = "Verification code must consist of exactly 6 numeric digits."
            });
        }

        _logger.LogInformation(
            "[BMS Auth API] Processing OTP verification for {Destination} (Purpose: {Purpose})",
            request.Destination,
            request.Purpose);

        var result = await _otpService.VerifyOtpAsync(request, ct);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
