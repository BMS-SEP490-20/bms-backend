using System.Security.Claims;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BMS.Api.Controllers.Auth;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>Đăng nhập bằng username/email + mật khẩu.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await auth.LoginAsync(request, GetClient(), ct));

    /// <summary>Đổi refresh token lấy cặp token mới (refresh token xoay vòng, dùng 1 lần).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
        => Ok(await auth.RefreshAsync(request.RefreshToken, GetClient(), ct));

    /// <summary>Đăng xuất phiên hiện tại (thu hồi refresh token).</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken ct)
    {
        await auth.LogoutAsync(CurrentUserId, request?.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Đăng xuất khỏi mọi thiết bị.</summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await auth.LogoutAllAsync(CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Thông tin người dùng hiện tại (đọc mới từ DB).</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserInfoDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserInfoDto>> Me(CancellationToken ct)
        => Ok(await auth.GetProfileAsync(CurrentUserId, ct));

    private string CurrentUserId =>
        User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();

    private ClientContext GetClient()
    {
        var ua = Request.Headers.UserAgent.ToString();
        if (ua.Length > 256) ua = ua[..256];
        return new ClientContext(HttpContext.Connection.RemoteIpAddress?.ToString(), ua);
    }
}
