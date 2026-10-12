using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Thực thể lưu trữ mã xác thực OTP trong MongoDB collection "otp_tokens".
/// Mã OTP không bao giờ lưu trữ dạng plaintext mà được mã hóa băm một chiều (Salted SHA-256)
/// để bảo vệ hệ thống trước nguy cơ rò rỉ dữ liệu (OWASP Password Storage Cheat Sheet).
/// Hệ thống kích hoạt TTL Index tại trường ExpiresAt để MongoDB tự động xóa bản ghi hết hạn sau 300 giây.
/// </summary>
public class OtpTokenEntity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Địa chỉ Email hoặc Số điện thoại của người nhận mã.
    /// </summary>
    [BsonElement("recipient")]
    public string Recipient { get; set; } = string.Empty;

    /// <summary>
    /// Chuỗi băm mã OTP (SHA-256 kèm Salt). Tuyệt đối không lưu mã OTP thô (plaintext).
    /// </summary>
    [BsonElement("hashedOtp")]
    public string HashedOtp { get; set; } = string.Empty;

    /// <summary>
    /// Chuỗi muối ngẫu nhiên sinh từ Cryptographically Secure PRNG để phòng chống Rainbow Table attack.
    /// </summary>
    [BsonElement("salt")]
    public string Salt { get; set; } = string.Empty;

    /// <summary>
    /// Mục đích của mã xác thực (Ví dụ: "Registration", "PasswordReset", "Login").
    /// </summary>
    [BsonElement("purpose")]
    public string Purpose { get; set; } = string.Empty;

    /// <summary>
    /// Kênh phát tán mã ("Email" hoặc "Sms").
    /// </summary>
    [BsonElement("channel")]
    public string Channel { get; set; } = "Email";

    /// <summary>
    /// Số lần người dùng đã nhập sai mã OTP. Giới hạn tối đa 5 lần trước khi vô hiệu hóa vĩnh viễn.
    /// </summary>
    [BsonElement("attemptCount")]
    public int AttemptCount { get; set; } = 0;

    /// <summary>
    /// Đánh dấu mã đã được xác thực thành công nhằm ngăn chặn tấn công phát lại (Replay Attack).
    /// </summary>
    [BsonElement("isUsed")]
    public bool IsUsed { get; set; } = false;

    /// <summary>
    /// Thời điểm khởi tạo mã xác thực (UTC).
    /// </summary>
    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm hết hạn hiệu lực của mã OTP (UTC).
    /// MongoDB sử dụng trường này làm TTL Index (ExpireAfter = TimeSpan.Zero) để tự động thu hồi tài nguyên.
    /// </summary>
    [BsonElement("expiresAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ExpiresAt { get; set; }
}
