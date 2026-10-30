using System;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.AuditLogs
{
    /// <summary>
    /// Data transfer object for creating a new audit log record.
    /// </summary>
    public class CreateAuditLogDto
    {
        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserRole { get; set; }

        [Required]
        public string Action { get; set; } = null!;

        public string Category { get; set; } = "OPERATIONAL"; // SECURITY, ACCESS, OPERATIONAL, DATA_CHANGE
        public string Severity { get; set; } = "INFO"; // INFO, WARNING, CRITICAL, ERROR
        public string? IpAddress { get; set; }
        public string? TargetEntity { get; set; }
        public string? EntityId { get; set; }

        [Required]
        public string Details { get; set; } = null!;

        public string Status { get; set; } = "SUCCESS";
        public string? Browser { get; set; }
    }

    /// <summary>
    /// Filter parameters for querying audit log history.
    /// </summary>
    public class AuditLogFilterDto
    {
        public string? Category { get; set; }
        public string? Severity { get; set; }
        public string? Status { get; set; }
        public string? Action { get; set; }
        public string? SearchTerm { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;

        /// <summary>Maps the HTTP filter to the persistence-agnostic Core query.</summary>
        public BMS.Core.Entities.AuditLogQuery ToQuery() =>
            new(Category, Severity, Status, Action, SearchTerm, FromDate, ToDate, PageNumber, PageSize);
    }
}

