using System.ComponentModel.DataAnnotations;
using BMS.Core.Enums;

namespace BMS.Application.DTOs.Auth;

/// <summary>
/// DTO tiếp nhận yêu cầu sinh và phát tán mã xác thực OTP (UC03).
/// Áp dụng Data Annotations để xác thực nghiêm ngặt dữ liệu đầu vào theo chuẩn OWASP Input Validation.
/// </summary>
public class SendOtpRequestDto
{
    /// <summary>
    /// Địa chỉ Email hoặc Số điện thoại người nhận.
    /// Bắt buộc không được để trống và phải đúng định dạng chuẩn viễn thông/thư điện tử.
    /// </summary>
    [Required(ErrorMessage = "Địa chỉ nhận mã OTP không được để trống.")]
    [StringLength(100, ErrorMessage = "Địa chỉ nhận mã không được vượt quá 100 ký tự.")]
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// Mục đích của việc yêu cầu mã xác thực. Mặc định là PasswordReset.
    /// </summary>
    [Required(ErrorMessage = "Mục đích xác thực là bắt buộc.")]
    public OtpPurpose Purpose { get; set; } = OtpPurpose.PasswordReset;

    /// <summary>
    /// Kênh truyền thông nhận mã (Email hoặc Sms). Mặc định là Email.
    /// </summary>
    public OtpChannel Channel { get; set; } = OtpChannel.Email;
}

/// <summary>
/// DTO phản hồi kết quả sau khi phát sinh mã OTP thành công.
/// </summary>
public class SendOtpResponseDto
{
    /// <summary>
    /// Trạng thái yêu cầu thành công hay thất bại.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Thông báo phản hồi thân thiện với người dùng.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Thời gian hiệu lực toàn phần của mã xác thực (tính bằng giây, mặc định 300s = 5 phút).
    /// </summary>
    public int ExpiresInSeconds { get; set; } = 300;

    /// <summary>
    /// Thời gian tối thiểu người dùng phải chờ trước khi được phép bấm gửi lại mã (chống Spam, mặc định 60 giây).
    /// </summary>
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Cờ đánh dấu nếu yêu cầu bị chặn bởi cơ chế giới hạn tần suất (Rate Limiting).
    /// </summary>
    public bool IsRateLimited { get; set; } = false;
}
