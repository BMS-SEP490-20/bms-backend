using System;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities
{
    /// <summary>
    /// Represents an audit trail record capturing security, access, and operational events.
    /// Viewable by Admin and Officer (Manager) roles.
    /// Persisted in the shared <c>audit_logs</c> collection (PascalCase element names,
    /// compatible with entries written by UC76 Test History).
    /// </summary>
    [BsonIgnoreExtraElements]
    public class AuditLog
    {
        /// <summary>Unique audit record identifier (e.g. <c>AUD-20261030120000-ABC123</c>); legacy ObjectId ids are read as strings.</summary>
        [Key]
        [BsonId]
        [BsonSerializer(typeof(FlexibleNullableStringObjectIdSerializer))]
        public string AuditId { get; set; } = null!;

        /// <summary>UTC time the event happened.</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>Account id of the actor, or <c>SYSTEM</c>.</summary>
        public string UserId { get; set; } = null!;

        /// <summary>Display name of the actor.</summary>
        public string UserName { get; set; } = null!;

        /// <summary>Role name of the actor at the time of the event.</summary>
        public string UserRole { get; set; } = null!;

        /// <summary>Action code, e.g. LOGIN, VIEW_RESULT.</summary>
        public string Action { get; set; } = null!;

        /// <summary>Event category.</summary>
        public string Category { get; set; } = "OPERATIONAL"; // SECURITY, ACCESS, OPERATIONAL, DATA_CHANGE

        /// <summary>Event severity.</summary>
        public string Severity { get; set; } = "INFO"; // INFO, WARNING, CRITICAL, ERROR

        /// <summary>Client IP address.</summary>
        public string IpAddress { get; set; } = "127.0.0.1";

        /// <summary>Domain entity type affected by the action.</summary>
        public string TargetEntity { get; set; } = "System";

        /// <summary>Identifier of the affected entity, if any.</summary>
        public string? EntityId { get; set; }

        /// <summary>Human-readable details. Must not contain unmasked PII.</summary>
        public string Details { get; set; } = null!;

        /// <summary>Outcome of the action.</summary>
        public string Status { get; set; } = "SUCCESS"; // SUCCESS, FAILED

        /// <summary>Client user agent, if known.</summary>
        public string? Browser { get; set; }
    }

    /// <summary>
    /// Persistence-agnostic filter for querying audit logs. Lives in Core so repository contracts
    /// do not depend on Application DTOs (Clean Architecture dependency rule).
    /// </summary>
    public sealed record AuditLogQuery(
        string? Category,
        string? Severity,
        string? Status,
        string? Action,
        string? SearchTerm,
        DateTime? FromDate,
        DateTime? ToDate,
        int PageNumber,
        int PageSize);

    /// <summary>Aggregated audit counters computed by the database.</summary>
    public sealed record AuditLogStatistics(long TotalLogs, long SecurityEvents, long AccessEvents, long CriticalAlerts, long FailedActions);
}
