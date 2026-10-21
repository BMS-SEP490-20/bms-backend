using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Backup;
using BMS.Application.DTOs.Roles;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for Database Backup and Restoration procedures (UC80 / BR-242).
/// Specifically manages archiving patient diagnostic test results and clinical documents.
/// Conforms to: Production Medical Standards, Cryptographic SHA-256 Validation & AGENTS-CODE.md.
/// </summary>
public interface IBackupService
{
    Task<(bool Success, string Message, BackupDetailDto? Data)> CreateBackupAsync(
        CreateBackupRequestDto dto,
        string operatorId,
        string operatorName,
        CancellationToken ct = default);

    Task<PagedResult<BackupListItemDto>> GetBackupsPagedAsync(
        BackupQueryParameters query,
        CancellationToken ct = default);

    Task<BackupDetailDto?> GetBackupByIdAsync(string id, CancellationToken ct = default);

    Task<VerifyIntegrityResultDto> VerifyIntegrityAsync(string id, CancellationToken ct = default);

    Task<RestoreValidationResultDto> ValidateRestoreDryRunAsync(
        string backupId,
        CancellationToken ct = default);

    Task<RestoreExecutionResultDto> ExecuteRestoreAsync(
        RestoreRequestDto dto,
        string operatorId,
        string operatorName,
        string operatorRole,
        CancellationToken ct = default);

    Task<(bool Success, Stream? FileStream, string FileName, string ContentType)> DownloadBackupAsync(
        string id,
        CancellationToken ct = default);

    Task<List<RestoreAuditLogDto>> GetRestoreHistoryAsync(
        string? backupId = null,
        CancellationToken ct = default);

    Task<BackupStatsDto> GetStatsAsync(CancellationToken ct = default);

    Task<bool> DeleteBackupAsync(string id, CancellationToken ct = default);
}
