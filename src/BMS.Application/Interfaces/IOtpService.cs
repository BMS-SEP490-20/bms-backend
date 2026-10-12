using BMS.Application.DTOs.Auth;

namespace BMS.Application.Interfaces;

/// <summary>
/// Giao diện xử lý nghiệp vụ sinh mã và kiểm tra mã xác thực OTP (UC03).
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Tiếp nhận yêu cầu, kiểm tra Rate Limiting, sinh mã bảo mật CSPRNG, băm và gửi mã qua kênh chỉ định.
    /// </summary>
    /// <param name="request">Thông tin yêu cầu gửi OTP.</param>
    /// <param name="clientIp">Địa chỉ IP người gọi để áp dụng Rate Limiting bảo vệ chống SMS Flooding.</param>
    /// <param name="ct">CancellationToken.</param>
    Task<SendOtpResponseDto> SendOtpAsync(SendOtpRequestDto request, string clientIp, CancellationToken ct = default);

    /// <summary>
    /// Xác thực mã OTP người dùng nhập vào so với chuỗi băm trong cơ sở dữ liệu.
    /// </summary>
    /// <param name="request">Thông tin xác thực OTP.</param>
    /// <param name="ct">CancellationToken.</param>
    Task<VerifyOtpResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request, CancellationToken ct = default);
}
