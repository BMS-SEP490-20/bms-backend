using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Entity representing a system user account.
/// Maps to the <c>accounts</c> collection in MongoDB.
/// Used across authentication, RBAC, and audit features.
/// </summary>
public class User
{
    /// <summary>
    /// Unique account identifier (MongoDB ObjectId).
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Login username. Must be unique across the system.
    /// </summary>
    [BsonElement("Username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Hashed password (BCrypt / ASP.NET Identity hash).
    /// Never store or return the plain-text password.
    /// </summary>
    [BsonElement("Password")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Account email address. Must be unique.
    /// </summary>
    [BsonElement("Email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Display full name of the account holder.
    /// </summary>
    [BsonElement("FullName")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Contact phone number (used for OTP and notifications).
    /// </summary>
    [BsonElement("PhoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key referencing the <c>roles</c> collection (_id).
    /// Determines the user's RBAC permissions.
    /// <list type="bullet">
    ///   <item>1 – Admin</item>
    ///   <item>2 – Doctor</item>
    ///   <item>3 – LabTechnician</item>
    ///   <item>4 – LabHead</item>
    ///   <item>5 – Nurse</item>
    ///   <item>6 – Receptionist</item>
    ///   <item>7 – Cashier</item>
    ///   <item>8 – WarehouseStaff</item>
    ///   <item>9 – Patient</item>
    /// </list>
    /// </summary>
    [BsonElement("RoleId")]
    public int RoleId { get; set; }

    /// <summary>
    /// Account status flag.
    /// <list type="bullet">
    ///   <item>0 – Inactive / Pending activation</item>
    ///   <item>1 – Active</item>
    ///   <item>2 – Locked / Suspended</item>
    /// </list>
    /// </summary>
    [BsonElement("Status")]
    public int Status { get; set; }

    /// <summary>
    /// Timestamp when the account was created (UTC).
    /// </summary>
    [BsonElement("CreatedAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp of the most recent successful login (UTC).
    /// Null if the user has never logged in.
    /// </summary>
    [BsonElement("LastLoginAt")]
    [BsonIgnoreIfNull]
    public DateTime? LastLoginAt { get; set; }
}