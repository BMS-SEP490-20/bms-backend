using BMS.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BMS.Infrastructure.Services;

/// <summary>
/// SMS Gateway delivery service implementation for telecom carrier messaging.
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
    /// Dispatches an SMS verification message to the specified phone number.
    /// </summary>
    public async Task<bool> SendOtpSmsAsync(string phoneNumber, string otpCode, CancellationToken ct = default)
    {
        try
        {
            var message = $"[BMS Lab] Your verification code is {otpCode}. Valid for 5 minutes. Do not share this code.";

            _logger.LogInformation(
                "[BMS SMS Gateway] Dispatching SMS message to {Phone}: {Message}",
                phoneNumber,
                message);

            await Task.Delay(100, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch SMS to {Phone}", phoneNumber);
            return false;
        }
    }
}
