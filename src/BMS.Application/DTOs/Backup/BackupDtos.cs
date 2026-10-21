using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BMS.Core.Entities;

namespace BMS.Application.DTOs.Backup;

/// <summary>
/// Query filter parameters for retrieving backups list (UC80 / BR-242).
/// </summary>
public class BackupQueryParameters
{
    public BackupScope? Scope { get; set; }
    public BackupStatus? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 15;
}

/// <summary>
/// Payload to initiate a new backup snapshot (UC80).
/// Specifically supports archiving patient diagnostic laboratory test data.
/// </summary>
public class CreateBackupRequestDto
{
    [Required(ErrorMessage = "Tên bản sao lưu không được để trống.")]
    [StringLength(150, ErrorMessage = "Tên bản sao lưu không vượt quá 150 ký tự.")]
    public string BackupName { get; set; } = string.Empty;

    public BackupScope Scope { get; set; } = BackupScope.PatientTestResults;

    public bool IncludePatientRecords { get; set; } = true;

    public bool IncludeTestResults { get; set; } = true;

    public bool IncludeBloodSamples { get; set; } = true;

    public bool IncludeClinicalAssessments { get; set; } = true;

    public string? Notes { get; set; }
}

/// <summary>
/// List item DTO for backup table directory.
/// </summary>
public class BackupListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string BackupId { get; set; } = string.Empty;
    public string BackupName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string BackupType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string FileSizeFormatted { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string? LastRestoredAt { get; set; }
}

/// <summary>
/// Detailed DTO for viewing full backup metadata and breakdown.
/// </summary>
public class BackupDetailDto
{
    public string Id { get; set; } = string.Empty;
    public string BackupId { get; set; } = string.Empty;
    public string BackupName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string BackupType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string FileSizeFormatted { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public Dictionary<string, int> CollectionBreakdown { get; set; } = new();
    public string EncryptionAlgorithm { get; set; } = string.Empty;
    public bool IsEncrypted { get; set; }
    public string? RetentionUntil { get; set; }
    public string? Notes { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
    public string? LastVerifiedAt { get; set; }
    public string? LastRestoredAt { get; set; }
    public string? LastRestoredBy { get; set; }
}

/// <summary>
/// Overall backup system statistics and KPI summary.
/// </summary>
public class BackupStatsDto
{
    public int TotalBackups { get; set; }
    public int CompletedBackups { get; set; }
    public int FailedBackups { get; set; }
    public int PatientTestScopeBackups { get; set; }
    public int TotalStorageMb { get; set; }
    public double IntegritySuccessRate { get; set; } = 100.0;
    public string? LastBackupTime { get; set; }
}

/// <summary>
/// Request payload to execute data restoration.
/// Requires a strict confirmation phrase to prevent inadvertent data loss.
/// </summary>
public class RestoreRequestDto
{
    [Required(ErrorMessage = "Bắt buộc chọn bản sao lưu.")]
    public string BackupId { get; set; } = string.Empty;

    public bool IsDryRun { get; set; } = false;

    public bool OverwriteExisting { get; set; } = true;

    [Required(ErrorMessage = "Bắt buộc nhập cụm từ xác nhận khôi phục.")]
    public string ConfirmationPhrase { get; set; } = string.Empty; // Must be "XAC_NHAN_KHOI_PHUC" or "CONFIRM_RESTORE"
}

/// <summary>
/// Result of dry-run pre-flight restoration validation.
/// </summary>
public class RestoreValidationResultDto
{
    public bool CanRestore { get; set; }
    public bool ChecksumValid { get; set; }
    public string ExpectedChecksum { get; set; } = string.Empty;
    public string CalculatedChecksum { get; set; } = string.Empty;
    public string BackupId { get; set; } = string.Empty;
    public string BackupName { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public int TotalRecordsToRestore { get; set; }
    public Dictionary<string, int> RecordSummary { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result returned after executing data restoration.
/// </summary>
public class RestoreExecutionResultDto
{
    public bool Success { get; set; }
    public string RestoreId { get; set; } = string.Empty;
    public string BackupId { get; set; } = string.Empty;
    public int RestoredCount { get; set; }
    public string Message { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public string ExecutedAt { get; set; } = string.Empty;
    public string ExecutedBy { get; set; } = string.Empty;
}

/// <summary>
/// Audit log DTO of past restoration events.
/// </summary>
public class RestoreAuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public string RestoreId { get; set; } = string.Empty;
    public string BackupId { get; set; } = string.Empty;
    public string RestoredBy { get; set; } = string.Empty;
    public string RestoredByRole { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool ChecksumVerified { get; set; }
    public int TotalRecordsRestored { get; set; }
    public bool IsDryRun { get; set; }
    public string? ValidationNotes { get; set; }
    public string? ErrorMessage { get; set; }
    public string ExecutedAt { get; set; } = string.Empty;
    public long DurationMs { get; set; }
}

/// <summary>
/// Result of cryptographic integrity check against a backup snapshot.
/// </summary>
public class VerifyIntegrityResultDto
{
    public bool IsValid { get; set; }
    public string BackupId { get; set; } = string.Empty;
    public string ExpectedChecksum { get; set; } = string.Empty;
    public string CalculatedChecksum { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string VerifiedAt { get; set; } = string.Empty;
}
