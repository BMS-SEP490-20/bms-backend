using BMS.Application.DTOs;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Auth;

/// <summary>
/// API Controller managing identity verification, authentication, and One-Time Password workflows (UC02, UC03, UC04, UC05).
/// Handles user registration, credentials authentication, cryptographic OTP dispatch, and 2FA verification.
/// Conforms to: RESTful API Design, OWASP ASVS & RFC 7807 Problem Details.
/// </summary>
[ApiController]
[Route("api/auth")]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IOtpService _otpService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IOtpService otpService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _otpService = otpService;
        _logger = logger;
    }

    /// <summary>
    /// UC02 - Đăng ký tài khoản bệnh nhân (Register Account)
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ.", errors = ModelState });
        }

        var result = await _authService.RegisterAsync(request);
        if (!result.Success)
        {
            return Conflict(result);
        }

        return StatusCode(201, result);
    }

    /// <summary>
    /// UC05 - Đăng nhập hệ thống (System Login)
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Vui lòng nhập tên đăng nhập và mật khẩu." });
        }

        var result = await _authService.LoginAsync(request);
        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// UC04 - Xác thực mã OTP email để kích hoạt tài khoản
    /// </summary>
    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyAccountOtp([FromBody] VerifyOtpDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Dữ liệu OTP không hợp lệ." });
        }

        var result = await _authService.VerifyOtpAsync(request);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Generates and dispatches a 6-digit OTP code via Email or SMS for Registration or Password Recovery (I1-011 / UC03).
    /// </summary>
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

        var clientIp = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? "Unknown-IP";

        _logger.LogInformation(
            "[BMS Auth API] Processing OTP dispatch to {Destination} (Purpose: {Purpose}) from IP {ClientIp}",
            request.Destination,
            request.Purpose,
            clientIp);

        var result = await _otpService.SendOtpAsync(request, clientIp, ct);

        if (result.IsRateLimited)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, result);
        }

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Verifies a 6-digit OTP code submitted by the user (I1-011 / UC03).
    /// </summary>
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

    /// <summary>
    /// Lấy thông tin tài khoản hiện tại từ header hoặc param (Fallback demo)
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser([FromQuery] string? userId)
    {
        var id = userId ?? "USR-PAT-01";
        var user = await _authService.GetUserByIdAsync(id);
        if (user == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy người dùng." });
        }

        return Ok(new { success = true, data = user });
    }
}
