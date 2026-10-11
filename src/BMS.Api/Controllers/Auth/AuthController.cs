using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
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
    /// UC04 - Xác thực mã OTP email để kích hoạt tài khoản
    /// </summary>
    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto request)
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
    /// UC05 - Đăng nhập hệ thống
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
    /// Lấy thông tin tài khoản hiện tại từ header hoặc param
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser([FromQuery] string? userId)
    {
        var id = userId ?? "USR-PAT-01"; // Fallback demo
        var user = await _authService.GetUserByIdAsync(id);
        if (user == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy người dùng." });
        }

        return Ok(new { success = true, data = user });
    }
}
