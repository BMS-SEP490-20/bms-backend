namespace BMS.Application.Interfaces;

/// <summary>
/// Giao diện dịch vụ phát tán tin nhắn SMS Brandname viễn thông.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Gửi tin nhắn SMS chứa mã OTP tới số điện thoại di động người dùng.
    /// </summary>
    /// <param name="phoneNumber">Số điện thoại di động (định dạng E.164 hoặc số nội địa Việt Nam).</param>
    /// <param name="otpCode">Mã OTP 6 chữ số.</param>
    /// <param name="ct">CancellationToken.</param>
    Task<bool> SendOtpSmsAsync(string phoneNumber, string otpCode, CancellationToken ct = default);
}
