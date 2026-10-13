using System.Security.Claims;
using BMS.Application.DTOs;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BMS.Api.Controllers.Auth;

[ApiController]
[Route("api/auth")]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
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

    // =========================================================
    // LOGIN - Giữ luồng JWT và refresh token của feature branch
    // =========================================================

    /// <summary>Đăng nhập bằng username/email và mật khẩu.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var result = await _authService.LoginAsync(
            request,
            GetClient(),
            ct);

        return Ok(result);
    }

    // =========================================================
    // REFRESH TOKEN
    // =========================================================

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken ct)
    {
        var result = await _authService.RefreshAsync(
            request.RefreshToken,
            GetClient(),
            ct);

        return Ok(result);
    }

    // =========================================================
    // LOGOUT
    // =========================================================

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest? request,
        CancellationToken ct)
    {
        await _authService.LogoutAsync(
            CurrentUserId,
            request?.RefreshToken,
            ct);

        return NoContent();
    }

    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await _authService.LogoutAllAsync(CurrentUserId, ct);

        return NoContent();
    }

    // =========================================================
    // REGISTER
    // =========================================================

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Dữ liệu không hợp lệ.",
                errors = ModelState
            });
        }

        var result = await _authService.RegisterAsync(request);

        if (!result.Success)
        {
            return Conflict(result);
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }

    // =========================================================
    // VERIFY ACCOUNT OTP - Giữ API cũ của main
    // =========================================================

    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyAccountOtp(
        [FromBody] VerifyOtpDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Dữ liệu OTP không hợp lệ."
            });
        }

        var result = await _authService.VerifyOtpAsync(request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    // =========================================================
    // SEND OTP - Registration / Forgot Password
    // =========================================================

    [HttpPost("otp/send")]
    [AllowAnonymous]
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

        var clientIp =
            HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? "Unknown-IP";

        _logger.LogInformation(
            "Processing OTP dispatch for {Destination}, purpose {Purpose}, IP {ClientIp}",
            request.Destination,
            request.Purpose,
            clientIp);

        var result = await _otpService.SendOtpAsync(
            request,
            clientIp,
            ct);

        if (result.IsRateLimited)
        {
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                result);
        }

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    // =========================================================
    // VERIFY OTP - Forgot Password / OTP workflow
    // =========================================================

    [HttpPost("otp/verify")]
    [AllowAnonymous]
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

        var result = await _otpService.VerifyOtpAsync(request, ct);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    // =========================================================
    // CURRENT USER - Đọc profile bằng JWT
    // =========================================================

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserInfoDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserInfoDto>> Me(CancellationToken ct)
    {
        var result = await _authService.GetProfileAsync(
            CurrentUserId,
            ct);

        return Ok(result);
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private string CurrentUserId =>
        User.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException();

    private ClientContext GetClient()
    {
        var userAgent = Request.Headers.UserAgent.ToString();

        if (userAgent.Length > 256)
        {
            userAgent = userAgent[..256];
        }

        return new ClientContext(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent);
    }
}
