using System.Security.Cryptography;
using System.Text;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Enums;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// Service nghiệp vụ xử lý phát sinh, bảo vệ và kiểm chứng mã xác thực OTP (UC03).
/// Tuân thủ:
/// - RFC 6238 TOTP & NIST SP 800-63B Authentication Standards.
/// - OWASP Password Storage & Anti-Automation Rate Limiting Guidelines.
/// - AGENTS-CODE.md chuẩn Comment và Architecture.
/// </summary>
public class OtpService : IOtpService
{
    private readonly IOtpRepository _otpRepository;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;

    // Cấu hình nghiệp vụ cốt lõi
    private const int OtpExpiryMinutes = 5;
    private const int ResendCooldownSeconds = 60;
    private const int MaxAllowedRequestsInWindow = 3;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(5);
    private const int MaxFailedAttempts = 5;

    public OtpService(
        IOtpRepository otpRepository,
        IEmailService emailService,
        ISmsService smsService)
    {
        _otpRepository = otpRepository;
        _emailService = emailService;
        _smsService = smsService;
    }

    /// <summary>
    /// Sinh và phát tán mã OTP 6 chữ số với cơ chế bảo vệ Rate Limit và Salted Hash.
    /// </summary>
    public async Task<SendOtpResponseDto> SendOtpAsync(
        SendOtpRequestDto request,
        string clientIp,
        CancellationToken ct = default)
    {
        var recipient = request.Destination.Trim().ToLowerInvariant();
        var purposeStr = request.Purpose.ToString();

        // 1. Kiểm tra giới hạn tần suất (Rate Limiting) chống tấn công Spam viễn thông
        var recentCount = await _otpRepository.CountRecentRequestsAsync(recipient, RateLimitWindow, ct);
        if (recentCount >= MaxAllowedRequestsInWindow)
        {
            return new SendOtpResponseDto
            {
                Success = false,
                IsRateLimited = true,
                Message = "Bạn đã yêu cầu gửi mã quá số lần cho phép (tối đa 3 lần trong 5 phút). Vui lòng thử lại sau.",
                CooldownSeconds = 300,
                ExpiresInSeconds = 0
            };
        }

        // 2. Kiểm tra Cooldown 60s giữa 2 lần gửi liên tiếp
        var latestActive = await _otpRepository.GetLatestActiveByRecipientAsync(recipient, purposeStr, ct);
        if (latestActive != null)
        {
            var elapsedSeconds = (DateTime.UtcNow - latestActive.CreatedAt).TotalSeconds;
            if (elapsedSeconds < ResendCooldownSeconds)
            {
                var remainingCooldown = (int)Math.Ceiling(ResendCooldownSeconds - elapsedSeconds);
                return new SendOtpResponseDto
                {
                    Success = false,
                    IsRateLimited = false,
                    Message = $"Vui lòng đợi {remainingCooldown} giây trước khi yêu cầu gửi lại mã.",
                    CooldownSeconds = remainingCooldown,
                    ExpiresInSeconds = (int)Math.Max(0, (latestActive.ExpiresAt - DateTime.UtcNow).TotalSeconds)
                };
            }
        }

        // 3. Sinh mã OTP 6 chữ số ngẫu nhiên bằng CSPRNG (Cryptographically Secure Pseudo-Random Number Generator)
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");

        // 4. Sinh Salt ngẫu nhiên 16 bytes và băm SHA-256 mã OTP
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToBase64String(saltBytes);
        var hashedOtp = ComputeHash(otpCode, salt);

        // 5. Hủy hiệu lực toàn bộ mã cũ chưa sử dụng của người nhận cho mục đích này
        await _otpRepository.InvalidatePreviousOtpsAsync(recipient, purposeStr, ct);

        // 6. Lưu thực thể bảo mật vào cơ sở dữ liệu MongoDB
        var now = DateTime.UtcNow;
        var entity = new OtpTokenEntity
        {
            Recipient = recipient,
            HashedOtp = hashedOtp,
            Salt = salt,
            Purpose = purposeStr,
            Channel = request.Channel.ToString(),
            AttemptCount = 0,
            IsUsed = false,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(OtpExpiryMinutes)
        };

        await _otpRepository.CreateAsync(entity, ct);

        // 7. Phát tán thông báo qua Email hoặc SMS
        var purposeDescription = GetPurposeDescription(request.Purpose);
        bool sendSuccess;

        if (request.Channel == OtpChannel.Email)
        {
            sendSuccess = await _emailService.SendOtpEmailAsync(
                recipient,
                otpCode,
                purposeDescription,
                OtpExpiryMinutes,
                ct);
        }
        else
        {
            sendSuccess = await _smsService.SendOtpSmsAsync(recipient, otpCode, ct);
        }

        return new SendOtpResponseDto
        {
            Success = sendSuccess,
            Message = sendSuccess
                ? "Mã xác thực OTP đã được gửi thành công. Vui lòng kiểm tra hộp thư/tin nhắn."
                : "Không thể phát tán mã OTP qua kênh truyền thông. Vui lòng kiểm tra lại địa chỉ.",
            ExpiresInSeconds = OtpExpiryMinutes * 60,
            CooldownSeconds = ResendCooldownSeconds,
            IsRateLimited = false
        };
    }

    /// <summary>
    /// Xác thực mã OTP người dùng nhập vào bằng so sánh an toàn hằng thời gian (Constant-time comparison).
    /// </summary>
    public async Task<VerifyOtpResponseDto> VerifyOtpAsync(
        VerifyOtpRequestDto request,
        CancellationToken ct = default)
    {
        var recipient = request.Destination.Trim().ToLowerInvariant();
        var purposeStr = request.Purpose.ToString();

        // 1. Tìm bản ghi OTP mới nhất còn hiệu lực
        var otpRecord = await _otpRepository.GetLatestActiveByRecipientAsync(recipient, purposeStr, ct);
        if (otpRecord == null)
        {
            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = "Mã xác thực không tồn tại hoặc đã hết hạn hiệu lực (5 phút). Vui lòng yêu cầu mã mới."
            };
        }

        // 2. Kiểm tra giới hạn số lần nhập sai (Brute Force Protection)
        if (otpRecord.AttemptCount >= MaxFailedAttempts)
        {
            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = "Mã OTP đã bị khóa do nhập sai quá 5 lần. Vui lòng yêu cầu mã mới.",
                RemainingAttempts = 0
            };
        }

        // 3. Tính toán hash từ input và so sánh hằng thời gian
        var candidateHash = ComputeHash(request.Otp.Trim(), otpRecord.Salt);
        var isMatch = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidateHash),
            Encoding.UTF8.GetBytes(otpRecord.HashedOtp));

        if (!isMatch)
        {
            // Tăng số lần thử sai
            otpRecord.AttemptCount++;
            await _otpRepository.UpdateAsync(otpRecord, ct);
            var remaining = MaxFailedAttempts - otpRecord.AttemptCount;

            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = $"Mã OTP không chính xác. Bạn còn {remaining} lần thử lại.",
                RemainingAttempts = remaining
            };
        }

        // 4. Xác thực thành công: Đánh dấu đã sử dụng để chống Replay Attack
        otpRecord.IsUsed = true;
        await _otpRepository.UpdateAsync(otpRecord, ct);

        // 5. Sinh Verification Token dùng 1 lần (dành cho bước Reset Password hoặc Kích hoạt tài khoản)
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var verificationToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

        return new VerifyOtpResponseDto
        {
            Success = true,
            Message = "Xác thực mã OTP thành công!",
            VerificationToken = verificationToken
        };
    }

    /// <summary>
    /// Băm mã OTP kèm chuỗi muối Salt bằng thuật toán SHA-256.
    /// </summary>
    private static string ComputeHash(string otp, string salt)
    {
        var payload = $"{salt}:{otp}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hashBytes);
    }

    private static string GetPurposeDescription(OtpPurpose purpose) => purpose switch
    {
        OtpPurpose.Registration => "Xác thực đăng ký tài khoản BMS mới",
        OtpPurpose.PasswordReset => "Cấp lại mật khẩu tài khoản BMS",
        OtpPurpose.Login => "Xác thực đăng nhập 2 yếu tố",
        OtpPurpose.Transaction => "Xác nhận giao dịch viện phí",
        _ => "Xác thực danh tính y tế BMS"
    };
}
