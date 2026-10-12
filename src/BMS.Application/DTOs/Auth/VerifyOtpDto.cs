using System.ComponentModel.DataAnnotations;
using BMS.Core.Enums;

namespace BMS.Application.DTOs.Auth;

/// <summary>
/// DTO tiếp nhận dữ liệu xác thực mã OTP do người dùng nhập từ giao diện Web/Mobile App.
/// </summary>
public class VerifyOtpRequestDto
{
    /// <summary>
    /// Địa chỉ Email hoặc Số điện thoại đã nhận mã OTP.
    /// </summary>
    [Required(ErrorMessage = "Địa chỉ liên lạc không được để trống.")]
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Chuỗi mã xác thực 6 chữ số do người dùng nhập vào.
    /// Bắt buộc phải là đúng 6 ký tự số từ 0-9.
    /// </summary>
    [Required(ErrorMessage = "Mã OTP không được để trống.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP phải bao gồm chính xác 6 chữ số.")]
    public string Otp { get; set; } = string.Empty;

    /// <summary>
    /// Mục đích nghiệp vụ ban đầu khi yêu cầu mã.
    /// </summary>
    [Required(ErrorMessage = "Mục đích xác thực là bắt buộc.")]
    public OtpPurpose Purpose { get; set; } = OtpPurpose.PasswordReset;
}

/// <summary>
/// DTO phản hồi kết quả sau khi kiểm tra mã OTP.
/// </summary>
public class VerifyOtpResponseDto
{
    /// <summary>
    /// Kết quả xác thực hợp lệ hay không.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Thông báo chi tiết kết quả.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Mã token chứng thực một lần (One-Time Verification Token) có ký số an toàn,
    /// dùng để chuyển giao qua bước Đặt lại mật khẩu hoặc Kích hoạt tài khoản mà không cần truyền lại mã OTP.
    /// </summary>
    public string? VerificationToken { get; set; }

    /// <summary>
    /// Số lần nhập sai còn lại trước khi tài khoản/mã bị khóa (nếu xác thực thất bại).
    /// </summary>
    public int? RemainingAttempts { get; set; }
}
