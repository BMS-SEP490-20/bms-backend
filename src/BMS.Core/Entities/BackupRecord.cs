using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Operational scope for data archiving and backups (UC80 / BR-242).
/// Specifically supports archiving patient diagnostic laboratory test data.
/// </summary>
public enum BackupScope
{
    PatientTestResults = 1,
    FullDatabase = 2,
    LaboratoryMasterData = 3,
    ClinicalRecords = 4
}

/// <summary>
/// Trigger method for the backup execution.
/// </summary>
public enum BackupType
{
    Manual = 1,
    ScheduledDaily = 2,
    AutomatedIncident = 3
}

/// <summary>
/// Execution and verification status of the backup file.
/// </summary>
public enum BackupStatus
{
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Restored = 4,
    Corrupted = 5
}

/// <summary>
/// Execution status of the restoration procedure.
/// </summary>
public enum RestoreStatus
{
    Validating = 1,
    DryRunPassed = 2,
    Completed = 3,
    Failed = 4,
    RolledBack = 5
}

/// <summary>
/// Domain entity representing a secure database backup snapshot in the BMS Laboratory System (UC80).
/// Conforms to: Production Medical Standards, ISO 15189 Long-term Record Retention & AGENTS-CODE.md.
/// </summary>
public class BackupRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Unique business code for backup referencing (e.g. BKP-20261021-001).
    /// </summary>
    [BsonElement("backupId")]
    public string BackupId { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly label or title of the backup snapshot.
    /// </summary>
    [BsonElement("backupName")]
    public string BackupName { get; set; } = string.Empty;

    /// <summary>
    /// Scope of archived records (PatientTestResults, FullDatabase, etc.).
    /// </summary>
    [BsonElement("scope")]
    public BackupScope Scope { get; set; } = BackupScope.PatientTestResults;

    /// <summary>
    /// Manual or automated scheduled backup.
    /// </summary>
    [BsonElement("backupType")]
    public BackupType BackupType { get; set; } = BackupType.Manual;

    /// <summary>
    /// Current integrity and processing status.
    /// </summary>
    [BsonElement("status")]
    public BackupStatus Status { get; set; } = BackupStatus.InProgress;

    /// <summary>
    /// Physical storage file path on the server or storage volume.
    /// </summary>
    [BsonElement("filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Archive file name on disk.
    /// </summary>
    [BsonElement("fileName")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Archive file size in bytes.
    /// </summary>
    [BsonElement("fileSizeBytes")]
    public long FileSizeBytes { get; set; } = 0;

    /// <summary>
    /// Cryptographic SHA-256 hash verifying data integrity and tampering prevention.
    /// </summary>
    [BsonElement("checksumSha256")]
    public string ChecksumSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Total aggregate count of patient test documents backed up.
    /// </summary>
    [BsonElement("totalRecords")]
    public int TotalRecords { get; set; } = 0;

    /// <summary>
    /// Breakdown of record count per clinical collection (e.g. TestResults: 120, BloodSamples: 45).
    /// </summary>
    [BsonElement("collectionBreakdown")]
    public Dictionary<string, int> CollectionBreakdown { get; set; } = new();

    /// <summary>
    /// Data encryption standard applied (e.g. AES-256 / SHA-256 Sealed Archive).
    /// </summary>
    [BsonElement("encryptionAlgorithm")]
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM / SHA-256";

    /// <summary>
    /// Indicates whether the archive payload is encrypted at rest.
    /// </summary>
    [BsonElement("isEncrypted")]
    public bool IsEncrypted { get; set; } = true;

    /// <summary>
    /// Retention period mandated by ISO 15189 (minimum 5-10 years for diagnostic records).
    /// </summary>
    [BsonElement("retentionUntil")]
    public DateTime? RetentionUntil { get; set; }

    /// <summary>
    /// Description or operational notes regarding the backup event.
    /// </summary>
    [BsonElement("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Creation timestamp in UTC.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Operator ID or system daemon that initiated the backup.
    /// </summary>
    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = "Hệ thống tự động";

    /// <summary>
    /// Completion timestamp in UTC.
    /// </summary>
    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Last verified timestamp.
    /// </summary>
    [BsonElement("lastVerifiedAt")]
    public DateTime? LastVerifiedAt { get; set; }

    /// <summary>
    /// Last restoration timestamp if this backup was restored.
    /// </summary>
    [BsonElement("lastRestoredAt")]
    public DateTime? LastRestoredAt { get; set; }

    /// <summary>
    /// Operator that executed the last restoration.
    /// </summary>
    [BsonElement("lastRestoredBy")]
    public string? LastRestoredBy { get; set; }
}

/// <summary>
/// Domain entity logging an audit trail for data restoration procedures (UC80 / BR-242).
/// Ensures accountability, integrity verification, and non-repudiation.
/// </summary>
public class RestoreAuditLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("restoreId")]
    public string RestoreId { get; set; } = string.Empty;

    [BsonElement("backupId")]
    public string BackupId { get; set; } = string.Empty;

    [BsonElement("restoredBy")]
    public string RestoredBy { get; set; } = string.Empty;

    [BsonElement("restoredByRole")]
    public string RestoredByRole { get; set; } = string.Empty;

    [BsonElement("status")]
    public RestoreStatus Status { get; set; } = RestoreStatus.Validating;

    [BsonElement("checksumVerified")]
    public bool ChecksumVerified { get; set; } = false;

    [BsonElement("totalRecordsRestored")]
    public int TotalRecordsRestored { get; set; } = 0;

    [BsonElement("isDryRun")]
    public bool IsDryRun { get; set; } = false;

    [BsonElement("validationNotes")]
    public string? ValidationNotes { get; set; }

    [BsonElement("errorMessage")]
    public string? ErrorMessage { get; set; }

    [BsonElement("executedAt")]
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("durationMs")]
    public long DurationMs { get; set; } = 0;
}
