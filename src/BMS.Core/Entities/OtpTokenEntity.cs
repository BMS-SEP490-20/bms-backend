using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Domain entity representing OTP verification tokens persisted in MongoDB collection "otp_tokens".
/// Raw OTP codes are NEVER stored in plaintext; only salted SHA-256 hashes are persisted to prevent
/// credential leakage in the event of database exfiltration (OWASP Password Storage Cheat Sheet).
/// Configured with a MongoDB TTL Index on ExpiresAt so that expired records are purged automatically after 300 seconds.
/// </summary>
public class OtpTokenEntity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Recipient email address or telephone number.
    /// </summary>
    [BsonElement("recipient")]
    public string Recipient { get; set; } = string.Empty;

    /// <summary>
    /// Salted SHA-256 cryptographic digest of the OTP code.
    /// </summary>
    [BsonElement("hashedOtp")]
    public string HashedOtp { get; set; } = string.Empty;

    /// <summary>
    /// Cryptographically secure random salt generated per token to mitigate rainbow table attacks.
    /// </summary>
    [BsonElement("salt")]
    public string Salt { get; set; } = string.Empty;

    /// <summary>
    /// Associated business purpose (e.g., "Registration", "PasswordReset", "Login").
    /// </summary>
    [BsonElement("purpose")]
    public string Purpose { get; set; } = string.Empty;

    /// <summary>
    /// Delivery channel ("Email" or "Sms").
    /// </summary>
    [BsonElement("channel")]
    public string Channel { get; set; } = "Email";

    /// <summary>
    /// Number of consecutive failed verification attempts. Capped at 5 before permanent invalidation.
    /// </summary>
    [BsonElement("attemptCount")]
    public int AttemptCount { get; set; } = 0;

    /// <summary>
    /// Indicates whether the token was already consumed to prevent replay attacks.
    /// </summary>
    [BsonElement("isUsed")]
    public bool IsUsed { get; set; } = false;

    /// <summary>
    /// Token creation timestamp (UTC).
    /// </summary>
    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Expiration timestamp (UTC).
    /// MongoDB enforces a TTL Index on this field (ExpireAfter = TimeSpan.Zero) to automatically reclaim resources.
    /// </summary>
    [BsonElement("expiresAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ExpiresAt { get; set; }
}
