using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BMS.Api.Controllers.Auth;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class PasswordsController(IPasswordResetService passwords) : ControllerBase
{
    /// <summary>
    /// Quên mật khẩu: gửi email chứa liên kết đặt lại mật khẩu.
    /// Luôn trả 200 với cùng một thông báo dù email có tồn tại hay không (chống dò tài khoản).
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("forgot-password")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MessageResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await passwords.ForgotPasswordAsync(request.Email, GetClient(), ct);
        return Ok(new MessageResponse(
            "Nếu email tồn tại trong hệ thống, chúng tôi đã gửi hướng dẫn đặt lại mật khẩu. Vui lòng kiểm tra hộp thư."));
    }

    /// <summary>Đặt mật khẩu mới bằng token trong email. Token dùng 1 lần; mọi phiên đăng nhập cũ bị thu hồi.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await passwords.ResetPasswordAsync(request.Token, request.NewPassword, ct);
        return NoContent();
    }

    private ClientContext GetClient()
    {
        var ua = Request.Headers.UserAgent.ToString();
        if (ua.Length > 256) ua = ua[..256];
        return new ClientContext(HttpContext.Connection.RemoteIpAddress?.ToString(), ua);
    }
}
