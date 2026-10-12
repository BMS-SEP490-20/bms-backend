using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Auth;

/// <summary>
/// Controller xử lý xác thực danh tính người dùng và quản lý mã OTP (UC03).
/// Tiếp nhận yêu cầu phát sinh mã xác thực và đối chiếu mã xác thực 2FA.
/// Tuân thủ: RESTful API Guidelines, OWASP Authentication Standards & RFC 7807 Problem Details.
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
    /// Gửi mã xác thực OTP 6 số qua Email hoặc SMS phục vụ Đăng ký hoặc Quên mật khẩu (I1-011).
    /// </summary>
    /// <param name="request">Thông tin người nhận và mục đích gửi mã.</param>
    /// <param name="ct">CancellationToken.</param>
    /// <response code="200">Gửi mã thành công, trả về thời gian hiệu lực và thời gian chờ gửi lại.</response>
    /// <response code="400">Dữ liệu yêu cầu không hợp lệ hoặc không đúng định dạng.</response>
    /// <response code="429">Vượt quá giới hạn tần suất gửi mã (tối đa 3 lần trong 5 phút).</response>
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
                Message = "Thông tin yêu cầu gửi OTP không hợp lệ."
            });
        }

        // Trích xuất địa chỉ IP người gửi để phòng chống SMS Flooding / Brute Force
        var clientIp = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? "Unknown-IP";

        _logger.LogInformation(
            "[BMS Auth API] Tiếp nhận yêu cầu gửi OTP tới {Destination} (Mục đích: {Purpose}) từ IP {ClientIp}",
            request.Destination,
            request.Purpose,
            clientIp);

        var result = await _otpService.SendOtpAsync(request, clientIp, ct);

        // Trường hợp vi phạm Rate Limit
        if (result.IsRateLimited)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, result);
        }

        // Trường hợp thất bại do lỗi kênh truyền thông hoặc dữ liệu
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Xác thực mã OTP 6 chữ số do người dùng nhập vào.
    /// </summary>
    /// <param name="request">Thông tin mã OTP và người nhận.</param>
    /// <param name="ct">CancellationToken.</param>
    /// <response code="200">Xác thực thành công, trả về Verification Token.</response>
    /// <response code="400">Mã OTP không chính xác, đã hết hạn hoặc bị khóa do nhập sai quá 5 lần.</response>
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
                Message = "Mã xác thực phải đúng định dạng 6 chữ số."
            });
        }

        _logger.LogInformation(
            "[BMS Auth API] Tiếp nhận yêu cầu xác thực OTP cho {Destination} (Mục đích: {Purpose})",
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
