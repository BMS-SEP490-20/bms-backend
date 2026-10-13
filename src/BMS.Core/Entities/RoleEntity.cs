using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Domain entity representing a Role and its granted permission matrix in RBAC security model (UC13).
/// Maps to MongoDB collection "roles".
/// Conforms to: NIST RBAC Standard & Enterprise Audit Trail.
/// </summary>
public class RoleEntity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("roleCode")]
    public string RoleCode { get; set; } = string.Empty;

    [BsonElement("roleName")]
    public string RoleName { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("permissions")]
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    /// System roles (Admin, Doctor, LabTechnician, etc.) cannot be deleted per rule BR-ROLE-01.
    /// </summary>
    [BsonElement("isSystemRole")]
    public bool IsSystemRole { get; set; } = false;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
