using System;
using System.ComponentModel.DataAnnotations;

namespace Testorder_service.Models.Entities
{
    /// <summary>
    /// Represents an audit trail record capturing security, access, and operational events.
    /// Viewable by Admin and Officer (Manager) roles.
    /// </summary>
    public class AuditLog
    {
        [Key]
        public string AuditId { get; set; } = null!;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public string UserId { get; set; } = null!;

        public string UserName { get; set; } = null!;

        public string UserRole { get; set; } = null!;

        public string Action { get; set; } = null!;

        public string Category { get; set; } = "OPERATIONAL"; // SECURITY, ACCESS, OPERATIONAL, DATA_CHANGE

        public string Severity { get; set; } = "INFO"; // INFO, WARNING, CRITICAL, ERROR

        public string IpAddress { get; set; } = "127.0.0.1";

        public string TargetEntity { get; set; } = "System";

        public string? EntityId { get; set; }

        public string Details { get; set; } = null!;

        public string Status { get; set; } = "SUCCESS"; // SUCCESS, FAILED

        public string? Browser { get; set; }
    }
}
