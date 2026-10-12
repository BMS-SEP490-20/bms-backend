namespace BMS.Application.Interfaces;

/// <summary>
/// Giao diện dịch vụ phát tán thư điện tử (Email) hệ thống BMS.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Gửi email chứa mã OTP kèm giao diện chuẩn y tế Medical Blue tới người nhận.
    /// </summary>
    /// <param name="toEmail">Địa chỉ hộp thư đến.</param>
    /// <param name="otpCode">Mã OTP 6 số thô (chỉ truyền qua kênh bảo mật TLS lúc gửi).</param>
    /// <param name="purposeDescription">Mô tả mục đích nghiệp vụ bằng tiếng Việt.</param>
    /// <param name="expiryMinutes">Số phút hiệu lực (mặc định 5 phút).</param>
    /// <param name="ct">CancellationToken.</param>
    Task<bool> SendOtpEmailAsync(
        string toEmail,
        string otpCode,
        string purposeDescription,
        int expiryMinutes = 5,
        CancellationToken ct = default);
}
