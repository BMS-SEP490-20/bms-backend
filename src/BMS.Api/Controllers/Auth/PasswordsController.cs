using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Auth;

[ApiController]
[Route("api/auth/passwords")]
public class PasswordsController : ControllerBase
{
    private readonly IAuthService _authService;

    public PasswordsController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// UC07 - Đổi mật khẩu tài khoản (Change Password)
    /// </summary>
    [HttpPost("change")]
    public async Task<IActionResult> ChangePassword([FromQuery] string? userId, [FromBody] ChangePasswordDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Dữ liệu đổi mật khẩu không hợp lệ.", errors = ModelState });
        }

        // In production this comes from HttpContext.User claim
        var targetUserId = userId ?? "USR-PAT-01";

        var result = await _authService.ChangePasswordAsync(targetUserId, request);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// UC10 - Quên mật khẩu / Yêu cầu gửi OTP đặt lại
    /// </summary>
    [HttpPost("forgot")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Email không hợp lệ." });
        }

        var result = await _authService.ForgotPasswordAsync(request);
        if (!result.Success)
        {
            return NotFound(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// UC11 - Xác thực OTP và đặt lại mật khẩu mới
    /// </summary>
    [HttpPost("reset")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Dữ liệu đặt lại mật khẩu không hợp lệ." });
        }

        var result = await _authService.ResetPasswordAsync(request);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
