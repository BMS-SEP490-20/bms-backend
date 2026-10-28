using BMS.Application.DTOs;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BMS.Api.Controllers.Auth;

[ApiController]
[Route("api/auth/passwords")]
[Route("api/auth")]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class PasswordsController : ControllerBase
{
    private readonly IPasswordResetService _passwords;
    private readonly IAuthService _authService;

    public PasswordsController(
        IPasswordResetService passwords,
        IAuthService authService)
    {
        _passwords = passwords;
        _authService = authService;
    }

    // =========================================================
    // UC07 - CHANGE PASSWORD
    // =========================================================

    /// <summary>
    /// Đổi mật khẩu tài khoản hiện tại.
    /// Production: lấy userId từ JWT, không tin userId do client gửi lên.
    /// </summary>
    [HttpPost("change")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Dữ liệu đổi mật khẩu không hợp lệ.",
                errors = ModelState
            });
        }

        var userId =
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "Không xác định được tài khoản hiện tại."
            });
        }

        var result = await _authService.ChangePasswordAsync(
            userId,
            request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    // =========================================================
    // FORGOT PASSWORD - EMAIL RESET TOKEN & OTP DISPATCH
    // =========================================================

    /// <summary>
    /// Gửi email hướng dẫn đặt lại mật khẩu và mã OTP xác thực (UC10 / BR-032).
    /// Luôn trả cùng thông báo để hạn chế dò tìm tài khoản (OWASP Anti-Enumeration).
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("forgot-password")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MessageResponse>> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken ct)
    {
        await _passwords.ForgotPasswordAsync(
            request.Email,
            GetClient(),
            ct);

        // Also generate and dispatch 6-digit OTP for users on the OTP verification interface (UC10/UC11)
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            try
            {
                await _authService.ForgotPasswordAsync(new ForgotPasswordDto { Email = request.Email.Trim() });
            }
            catch
            {
                // Fallback to maintain anti-enumeration response
            }
        }

        return Ok(new MessageResponse(
            "Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi."));
    }

    /// <summary>
    /// Đặt mật khẩu mới bằng token trong email.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken ct)
    {
        await _passwords.ResetPasswordAsync(
            request.Token,
            request.NewPassword,
            ct);

        return NoContent();
    }

    // =========================================================
    // LEGACY / MAIN - FORGOT PASSWORD VIA OTP
    // =========================================================

    /// <summary>
    /// Yêu cầu quên mật khẩu theo luồng OTP hiện có của main.
    /// </summary>
    [HttpPost("forgot")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPasswordOtp(
        [FromBody] ForgotPasswordDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Email không hợp lệ."
            });
        }

        var result = await _authService.ForgotPasswordAsync(request);

        if (!result.Success)
        {
            return NotFound(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Đặt lại mật khẩu bằng OTP theo luồng của main.
    /// </summary>
    [HttpPost("reset")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPasswordOtp(
        [FromBody] ResetPasswordDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Dữ liệu đặt lại mật khẩu không hợp lệ."
            });
        }

        var result = await _authService.ResetPasswordAsync(request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    // =========================================================
    // HELPER
    // =========================================================

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
