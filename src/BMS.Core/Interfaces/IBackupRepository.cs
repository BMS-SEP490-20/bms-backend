using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for backup and restoration management (UC80 / BR-242).
/// Conforms to: Production Medical Standards, Cryptographic Data Integrity & AGENTS-CODE.md.
/// </summary>
public interface IBackupRepository
{
    Task<BackupRecord?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<BackupRecord?> GetByBackupIdAsync(string backupId, CancellationToken ct = default);

    Task<(List<BackupRecord> Items, int TotalCount)> GetPagedAsync(
        BackupScope? scope = null,
        BackupStatus? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int pageNumber = 1,
        int pageSize = 15,
        CancellationToken ct = default);

    Task<BackupRecord> AddAsync(BackupRecord record, CancellationToken ct = default);

    Task<BackupRecord> UpdateAsync(BackupRecord record, CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    Task<RestoreAuditLog> AddRestoreAuditAsync(RestoreAuditLog log, CancellationToken ct = default);

    Task<List<RestoreAuditLog>> GetRestoreHistoryAsync(string? backupId = null, CancellationToken ct = default);

    Task<Dictionary<string, int>> GetBackupStatsAsync(CancellationToken ct = default);
}
