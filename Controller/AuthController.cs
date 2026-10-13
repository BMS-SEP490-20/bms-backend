using Microsoft.AspNetCore.Mvc;
using IamService.Services.Interface;
using iam_service.Modals.Dto;
using Microsoft.AspNetCore.Authorization;

namespace iam_service.Controller
{
    [ApiController]
    [Route("IAM/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        // Đăng nhập
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            var response = await _authService.LoginAsync(loginDto.username, loginDto.password);
            if (response == null || string.IsNullOrEmpty(response.Token))
            {
                return NotFound(new { message = "Sai username hoặc password" });
            }
            return Ok(new { message = "Đăng nhập thành công.", data = response });
        }

        // Đăng ký nhanh bằng email
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] string email)
        {
            var message = await _authService.RegisterAsync(email);
            if (string.IsNullOrEmpty(message))
            {
                return NotFound(new { message = "Tài khoản đã tồn tại !" });
            }
            return Ok(new { message = "Đăng ký thành công, username và password đã được gửi vào email.", data = message });
        }

        // Gửi OTP reset password
        [HttpPost("otp/send")]
        public async Task<IActionResult> SendOtp([FromBody] string request)
        {
            var success = await _authService.SendResetPasswordOtpAsync(request);
            if (!success)
            {
                return NotFound(new { message = "Không gửi được OTP. Email không hợp lệ hoặc không tồn tại." });
            }
            return Ok(new { message = "Gửi OTP thành công.", isSuccess = success });
        }

        // Xác thực OTP và đổi mật khẩu mới
        [HttpPost("otp/verify")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordSimple request)
        {
            var success = await _authService.ResetPasswordAsync(request);
            if (!success)
            {
                return NotFound(new { message = "OTP không hợp lệ hoặc đổi mật khẩu thất bại." });
            }
            return Ok(new { message = "Đổi mật khẩu thành công.", isSuccess = success });
        }
        [HttpPost("otp/sendotp")]
        public async Task<IActionResult> SendEmailOtp([FromBody] string request)
        {
            var success = await _authService.SendOtpAsync(request);
            if (!success)
            {
                return NotFound(new { message = "Không gửi được OTP. Email không hợp lệ hoặc không tồn tại." });
            }
            return Ok(new { message = "Gửi OTP thành công.", isSuccess = success });
        }
        [HttpPost("otp/verifyotp")]
        public async Task<IActionResult> Verifyotp([FromBody] VerifyOtpDto request)
        {
            var success = await _authService.Verifyotp(request);
            if (!success)
            {
                return NotFound(new { message = "OTP không hợp lệ hoặc đổi mật khẩu thất bại." });
            }
            return Ok(new { message = "Đổi mật khẩu thành công.", isSuccess = success });
        }
    }
}
