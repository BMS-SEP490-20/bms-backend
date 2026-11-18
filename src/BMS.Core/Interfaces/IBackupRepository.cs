using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;
using MongoDB.Bson;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for backup, data integrity verification and restoration management (UC80 / BR-242).
/// Conforms to: ISO/IEC 27001:2022, ISO/IEC 25010:2023, ISO 15189:2022 and OWASP ASVS.
/// </summary>
public interface IBackupRepository
{
    /// <summary>
    /// Checks whether the primary database is online and reachable.
    /// Prevents false positive backups when the database is unavailable (BK-02).
    /// </summary>
    Task<bool> PingDatabaseAsync(CancellationToken ct = default);

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

    /// <summary>
    /// Exports actual documents from database collections according to backup scope.
    /// </summary>
    Task<Dictionary<string, List<BsonDocument>>> ExportCollectionsAsync(BackupScope scope, CancellationToken ct = default);

    /// <summary>
    /// Restores documents into target database collections.
    /// </summary>
    Task<Dictionary<string, int>> ImportCollectionsAsync(Dictionary<string, List<BsonDocument>> data, bool overwrite, CancellationToken ct = default);

    /// <summary>
    /// Verifies that key collections contain expected records after restore (BK-07).
    /// </summary>
    Task<Dictionary<string, int>> VerifyCollectionsRecordCountAsync(IEnumerable<string> collections, CancellationToken ct = default);

    /// <summary>
    /// Enforces the backup retention policy (retains latest N backups, default 7).
    /// </summary>
    Task<int> EnforceRetentionPolicyAsync(int maxRetainedBackups = 7, CancellationToken ct = default);
}
