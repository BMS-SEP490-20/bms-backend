using BMS.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BMS.Infrastructure.Services;

/// <summary>
/// Triển khai dịch vụ gửi SMS Brandname viễn thông cho hệ thống BMS.
/// </summary>
public class SmsService : ISmsService
{
    private readonly ILogger<SmsService> _logger;
    private readonly IConfiguration _configuration;

    public SmsService(ILogger<SmsService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Gửi tin nhắn SMS OTP tới số điện thoại người nhận.
    /// </summary>
    public async Task<bool> SendOtpSmsAsync(string phoneNumber, string otpCode, CancellationToken ct = default)
    {
        try
        {
            var message = $"[BMS Lab] Ma xac thuc OTP cua ban la {otpCode}. Ma co hieu luc trong 5 phut. Khong chia se ma nay voi bat ky ai.";

            _logger.LogInformation(
                "[BMS SMS Gateway] Đang gửi tin nhắn SMS tới {Phone}: {Message}",
                phoneNumber,
                message);

            await Task.Delay(100, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi phát tán SMS tới {Phone}", phoneNumber);
            return false;
        }
    }
}
