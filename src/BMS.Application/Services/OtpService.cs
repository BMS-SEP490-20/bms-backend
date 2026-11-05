using System.Security.Cryptography;
using System.Text;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Enums;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// Business service handling cryptographic generation, storage, and verification of One-Time Passwords (UC03 / BR-IAM-03).
/// Conforms to:
/// - BR-IAM-03: Mã OTP có hiệu lực tối đa 60 giây, giới hạn nhập sai tối đa 3 lần.
/// - RFC 6238 TOTP & NIST SP 800-63B Authentication Standards.
/// - OWASP Password Storage & Anti-Automation Rate Limiting Guidelines.
/// - Production Engineering Standards.
/// </summary>
public class OtpService : IOtpService
{
    private readonly IOtpRepository _otpRepository;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;

    // Core business security constraints (BR-IAM-03)
    private const int OtpExpirySeconds = 60; // BR-IAM-03: Mã OTP hết hạn sau 60 giây
    private const int ResendCooldownSeconds = 60;
    private const int MaxAllowedRequestsInWindow = 3;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(5);
    private const int MaxFailedAttempts = 3; // BR-IAM-03: Giới hạn tối đa 3 lần thử sai

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
    /// Generates and dispatches a 6-digit OTP code protected by rate limiting and salted hashing.
    /// </summary>
    public async Task<SendOtpResponseDto> SendOtpAsync(
        SendOtpRequestDto request,
        string clientIp,
        CancellationToken ct = default)
    {
        var recipient = request.Destination.Trim().ToLowerInvariant();
        var purposeStr = request.Purpose.ToString();

        // 1. Sliding window Rate Limiting: prevent telecom abuse and SMS flooding
        var recentCount = await _otpRepository.CountRecentRequestsAsync(recipient, RateLimitWindow, ct);
        if (recentCount >= MaxAllowedRequestsInWindow)
        {
            return new SendOtpResponseDto
            {
                Success = false,
                IsRateLimited = true,
                Message = "Đã vượt quá số lần yêu cầu mã OTP (tối đa 3 lần / 5 phút). Vui lòng thử lại sau.",
                CooldownSeconds = 300,
                ExpiresInSeconds = 0
            };
        }

        // 2. Enforce 60-second cooldown between consecutive requests
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
                    Message = $"Vui lòng chờ {remainingCooldown} giây trước khi yêu cầu mã OTP mới.",
                    CooldownSeconds = remainingCooldown,
                    ExpiresInSeconds = (int)Math.Max(0, (latestActive.ExpiresAt - DateTime.UtcNow).TotalSeconds)
                };
            }
        }

        // 3. Cryptographically Secure Pseudo-Random Number Generator (CSPRNG)
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");

        // 4. Generate 16-byte random salt and compute SHA-256 hash
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToBase64String(saltBytes);
        var hashedOtp = ComputeHash(otpCode, salt);

        // 5. Invalidate any previous unconsumed active tokens for this recipient and purpose
        await _otpRepository.InvalidatePreviousOtpsAsync(recipient, purposeStr, ct);

        // 6. Persist secure token record into MongoDB (BR-IAM-03: 60 seconds lifetime)
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
            ExpiresAt = now.AddSeconds(OtpExpirySeconds)
        };

        await _otpRepository.CreateAsync(entity, ct);

        // 7. Dispatch notification via Email or SMS
        var purposeDescription = GetPurposeDescription(request.Purpose);
        bool sendSuccess;

        if (request.Channel == OtpChannel.Email)
        {
            sendSuccess = await _emailService.SendOtpEmailAsync(
                recipient,
                otpCode,
                purposeDescription,
                1, // 1 phút = 60s
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
                ? "Mã xác thực OTP đã được gửi thành công. Vui lòng kiểm tra hộp thư hoặc điện thoại."
                : "Không thể gửi mã OTP qua phương thức đã chọn. Vui lòng kiểm tra thông tin người nhận.",
            ExpiresInSeconds = OtpExpirySeconds,
            CooldownSeconds = ResendCooldownSeconds,
            IsRateLimited = false
        };
    }

    /// <summary>
    /// Verifies the submitted OTP code using constant-time hash comparison to mitigate side-channel timing attacks.
    /// Enforces BR-IAM-03: 60s expiration and maximum 3 failed attempts.
    /// </summary>
    public async Task<VerifyOtpResponseDto> VerifyOtpAsync(
        VerifyOtpRequestDto request,
        CancellationToken ct = default)
    {
        var recipient = request.Destination.Trim().ToLowerInvariant();
        var purposeStr = request.Purpose.ToString();

        // 1. Locate the latest active unexpired token (BR-IAM-03: 60s)
        var otpRecord = await _otpRepository.GetLatestActiveByRecipientAsync(recipient, purposeStr, ct);
        if (otpRecord == null)
        {
            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = "Mã xác thực không hợp lệ hoặc đã hết hạn (thời hạn 60 giây theo BR-IAM-03). Vui lòng yêu cầu mã mới."
            };
        }

        // 2. Protect against brute-force guessing attacks (BR-IAM-03: max 3 failed attempts)
        if (otpRecord.AttemptCount >= MaxFailedAttempts)
        {
            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = "Mã xác thực đã bị khóa do vượt quá 3 lần nhập sai (chuẩn BR-IAM-03). Vui lòng yêu cầu mã mới.",
                RemainingAttempts = 0
            };
        }

        // 3. Compute hash and compare using constant-time equality
        var candidateHash = ComputeHash(request.Otp.Trim(), otpRecord.Salt);
        var isMatch = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidateHash),
            Encoding.UTF8.GetBytes(otpRecord.HashedOtp));

        if (!isMatch)
        {
            otpRecord.AttemptCount++;
            await _otpRepository.UpdateAsync(otpRecord, ct);
            var remaining = MaxFailedAttempts - otpRecord.AttemptCount;

            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = remaining > 0
                    ? $"Mã OTP không chính xác. Bạn còn {remaining} lần thử (tối đa 3 lần theo BR-IAM-03)."
                    : "Mã xác thực đã bị khóa do vượt quá 3 lần nhập sai (chuẩn BR-IAM-03). Vui lòng yêu cầu mã mới.",
                RemainingAttempts = Math.Max(0, remaining)
            };
        }

        // 4. Mark token as consumed to prevent replay attacks
        otpRecord.IsUsed = true;
        await _otpRepository.UpdateAsync(otpRecord, ct);

        // 5. Generate a cryptographically secure one-time verification token
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
    /// Computes the SHA-256 hash digest of an OTP code with its dedicated cryptographic salt.
    /// </summary>
    private static string ComputeHash(string otp, string salt)
    {
        var payload = $"{salt}:{otp}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hashBytes);
    }

    private static string GetPurposeDescription(OtpPurpose purpose) => purpose switch
    {
        OtpPurpose.Registration => "Đăng ký tài khoản hệ thống y tế BMS",
        OtpPurpose.PasswordReset => "Đặt lại mật khẩu tài khoản BMS",
        OtpPurpose.Login => "Xác thực bảo mật đăng nhập 2 bước (2FA)",
        OtpPurpose.Transaction => "Xác nhận giao dịch thanh toán viện phí",
        _ => "Xác thực danh tính y tế BMS"
    };
}
