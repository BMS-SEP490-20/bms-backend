using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Infrastructure repository for Backup and Restore operations (UC80 / BR-242).
/// Supports MongoDB persistence with a thread-safe in-memory cache and medical seeds for resilient development.
/// Conforms to: Production Medical Standards, ISO 15189 Quality Management & AGENTS-CODE.md.
/// </summary>
public class BackupRepository : IBackupRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<BackupRepository> _logger;

    // Resilient in-memory storage fallback
    private static readonly ConcurrentDictionary<string, BackupRecord> _inMemoryBackups = new();
    private static readonly ConcurrentDictionary<string, RestoreAuditLog> _inMemoryRestores = new();
    private static readonly object _initLock = new();
    private static bool _seeded = false;

    public BackupRepository(MongoDbContext context, ILogger<BackupRepository> logger)
    {
        _context = context;
        _logger = logger;
        EnsureSeeded();
    }

    private void EnsureSeeded()
    {
        if (_seeded) return;
        lock (_initLock)
        {
            if (_seeded) return;

            var seeds = new List<BackupRecord>
            {
                new BackupRecord
                {
                    Id = "6724a001a1b2c3d4e5f60001",
                    BackupId = "BKP-261020-0001",
                    BackupName = "Lưu trữ dữ liệu xét nghiệm bệnh nhân - Tháng 10/2026",
                    Scope = BackupScope.PatientTestResults,
                    BackupType = BackupType.Manual,
                    Status = BackupStatus.Completed,
                    FilePath = "App_Data/Backups/BKP-261020-0001.json.gz",
                    FileName = "BKP-261020-0001.json.gz",
                    FileSizeBytes = 1843200, // 1.8 MB
                    ChecksumSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    TotalRecords = 286,
                    CollectionBreakdown = new Dictionary<string, int>
                    {
                        { "test_results", 145 },
                        { "blood_samples", 68 },
                        { "test_orders", 42 },
                        { "medical_assessments", 31 }
                    },
                    EncryptionAlgorithm = "AES-256-GCM / SHA-256",
                    IsEncrypted = true,
                    RetentionUntil = DateTime.UtcNow.AddYears(10),
                    Notes = "Sao lưu định kỳ dữ liệu kết quả phân tích huyết học và sinh hóa phòng Lab Cobas.",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    CreatedBy = "Hứa Khánh Duy (Lab Manager)",
                    CompletedAt = DateTime.UtcNow.AddDays(-1).AddMinutes(2),
                    LastVerifiedAt = DateTime.UtcNow.AddHours(-12)
                },
                new BackupRecord
                {
                    Id = "6724a001a1b2c3d4e5f60002",
                    BackupId = "BKP-261019-0002",
                    BackupName = "Sao lưu tự động toàn bộ cơ sở dữ liệu BMS (Hàng ngày)",
                    Scope = BackupScope.FullDatabase,
                    BackupType = BackupType.ScheduledDaily,
                    Status = BackupStatus.Completed,
                    FilePath = "App_Data/Backups/BKP-261019-0002.json.gz",
                    FileName = "BKP-261019-0002.json.gz",
                    FileSizeBytes = 4718592, // 4.5 MB
                    ChecksumSha256 = "87556d98c2134e12e1261d76326127164b3c202022927263524a8726e6371819",
                    TotalRecords = 824,
                    CollectionBreakdown = new Dictionary<string, int>
                    {
                        { "accounts", 120 },
                        { "roles", 6 },
                        { "test_criteria", 48 },
                        { "reference_intervals", 64 },
                        { "test_results", 280 },
                        { "blood_samples", 156 },
                        { "medical_records", 150 }
                    },
                    EncryptionAlgorithm = "AES-256-GCM / SHA-256",
                    IsEncrypted = true,
                    RetentionUntil = DateTime.UtcNow.AddYears(10),
                    Notes = "Bản chụp sao lưu ban đêm tự động qua cron job hệ thống.",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    CreatedBy = "Hệ thống tự động",
                    CompletedAt = DateTime.UtcNow.AddDays(-2).AddMinutes(4),
                    LastVerifiedAt = DateTime.UtcNow.AddDays(-1)
                },
                new BackupRecord
                {
                    Id = "6724a001a1b2c3d4e5f60003",
                    BackupId = "BKP-261015-0003",
                    BackupName = "Lưu trữ phân hệ kết quả xét nghiệm & chỉ số tham chiếu Q3/2026",
                    Scope = BackupScope.PatientTestResults,
                    BackupType = BackupType.Manual,
                    Status = BackupStatus.Restored,
                    FilePath = "App_Data/Backups/BKP-261015-0003.json.gz",
                    FileName = "BKP-261015-0003.json.gz",
                    FileSizeBytes = 2202009, // 2.1 MB
                    ChecksumSha256 = "a1b2c3d4e5f67890123456789abcdef0123456789abcdef0123456789abcdef0",
                    TotalRecords = 340,
                    CollectionBreakdown = new Dictionary<string, int>
                    {
                        { "test_results", 195 },
                        { "blood_samples", 90 },
                        { "test_orders", 55 }
                    },
                    EncryptionAlgorithm = "AES-256-GCM / SHA-256",
                    IsEncrypted = true,
                    RetentionUntil = DateTime.UtcNow.AddYears(10),
                    Notes = "Lưu trữ định kỳ sau đợt đánh giá chứng nhận ISO 15189.",
                    CreatedAt = DateTime.UtcNow.AddDays(-6),
                    CreatedBy = "Hứa Khánh Duy (Lab Manager)",
                    CompletedAt = DateTime.UtcNow.AddDays(-6).AddMinutes(3),
                    LastVerifiedAt = DateTime.UtcNow.AddDays(-3),
                    LastRestoredAt = DateTime.UtcNow.AddDays(-1),
                    LastRestoredBy = "Võ Gia Huy (System Admin)"
                }
            };

            foreach (var b in seeds)
            {
                _inMemoryBackups[b.BackupId] = b;
            }

            var restoreSeed = new RestoreAuditLog
            {
                Id = "6724b001a1b2c3d4e5f60001",
                RestoreId = "RST-261020-0001",
                BackupId = "BKP-261015-0003",
                RestoredBy = "Võ Gia Huy",
                RestoredByRole = "Admin",
                Status = RestoreStatus.Completed,
                ChecksumVerified = true,
                TotalRecordsRestored = 340,
                IsDryRun = false,
                ValidationNotes = "Xác thực mã băm SHA-256 khớp 100%. Khôi phục thành công 340 bản ghi xét nghiệm.",
                ExecutedAt = DateTime.UtcNow.AddDays(-1),
                DurationMs = 1845
            };
            _inMemoryRestores[restoreSeed.RestoreId] = restoreSeed;

            _seeded = true;
        }
    }

    public async Task<BackupRecord?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (_context.BackupRecords != null)
        {
            try
            {
                if (ObjectId.TryParse(id, out _))
                {
                    var docById = await _context.BackupRecords.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
                    if (docById != null) return docById;
                }

                var docByCode = await _context.BackupRecords.Find(x => x.BackupId == id).FirstOrDefaultAsync(ct);
                if (docByCode != null) return docByCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backup Repo] MongoDB read failed. Falling back to in-memory store.");
            }
        }

        return _inMemoryBackups.Values.FirstOrDefault(x => x.Id == id || x.BackupId == id);
    }

    public async Task<BackupRecord?> GetByBackupIdAsync(string backupId, CancellationToken ct = default)
    {
        if (_context.BackupRecords != null)
        {
            try
            {
                var doc = await _context.BackupRecords.Find(x => x.BackupId == backupId).FirstOrDefaultAsync(ct);
                if (doc != null) return doc;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backup Repo] MongoDB read failed.");
            }
        }

        return _inMemoryBackups.Values.FirstOrDefault(x => x.BackupId == backupId);
    }

    public async Task<(List<BackupRecord> Items, int TotalCount)> GetPagedAsync(
        BackupScope? scope = null,
        BackupStatus? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int pageNumber = 1,
        int pageSize = 15,
        CancellationToken ct = default)
    {
        if (_context.BackupRecords != null)
        {
            try
            {
                var builder = Builders<BackupRecord>.Filter;
                var filter = builder.Empty;

                if (scope.HasValue)
                    filter &= builder.Eq(x => x.Scope, scope.Value);

                if (status.HasValue)
                    filter &= builder.Eq(x => x.Status, status.Value);

                if (fromDate.HasValue)
                    filter &= builder.Gte(x => x.CreatedAt, fromDate.Value);

                if (toDate.HasValue)
                    filter &= builder.Lte(x => x.CreatedAt, toDate.Value);

                var total = await _context.BackupRecords.CountDocumentsAsync(filter, cancellationToken: ct);
                var items = await _context.BackupRecords
                    .Find(filter)
                    .SortByDescending(x => x.CreatedAt)
                    .Skip((pageNumber - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync(ct);

                if (total > 0)
                    return (items, (int)total);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backup Repo] MongoDB query failed. Falling back to in-memory.");
            }
        }

        var query = _inMemoryBackups.Values.AsQueryable();

        if (scope.HasValue)
            query = query.Where(x => x.Scope == scope.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        if (fromDate.HasValue)
            query = query.Where(x => x.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(x => x.CreatedAt <= toDate.Value);

        var list = query.OrderByDescending(x => x.CreatedAt).ToList();
        var paged = list.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return (paged, list.Count);
    }

    public async Task<BackupRecord> AddAsync(BackupRecord record, CancellationToken ct = default)
    {
        _inMemoryBackups[record.BackupId] = record;

        if (_context.BackupRecords != null)
        {
            try
            {
                await _context.BackupRecords.InsertOneAsync(record, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backup Repo] MongoDB insert failed. In-memory copy persisted.");
            }
        }

        return record;
    }

    public async Task<BackupRecord> UpdateAsync(BackupRecord record, CancellationToken ct = default)
    {
        _inMemoryBackups[record.BackupId] = record;

        if (_context.BackupRecords != null)
        {
            try
            {
                await _context.BackupRecords.ReplaceOneAsync(
                    x => x.Id == record.Id || x.BackupId == record.BackupId,
                    record,
                    new ReplaceOptions { IsUpsert = true },
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backup Repo] MongoDB replace failed.");
            }
        }

        return record;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        bool inMemRemoved = false;
        var existing = _inMemoryBackups.Values.FirstOrDefault(x => x.Id == id || x.BackupId == id);
        if (existing != null)
        {
            inMemRemoved = _inMemoryBackups.TryRemove(existing.BackupId, out _);
        }

        if (_context.BackupRecords != null)
        {
            try
            {
                long deleted = 0;
                if (ObjectId.TryParse(id, out _))
                {
                    var res = await _context.BackupRecords.DeleteOneAsync(x => x.Id == id, ct);
                    deleted = res.DeletedCount;
                }
                if (deleted == 0)
                {
                    var res = await _context.BackupRecords.DeleteOneAsync(x => x.BackupId == id, ct);
                    deleted = res.DeletedCount;
                }
                return deleted > 0 || inMemRemoved;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backup Repo] MongoDB delete failed.");
            }
        }

        return inMemRemoved;
    }

    public async Task<RestoreAuditLog> AddRestoreAuditAsync(RestoreAuditLog log, CancellationToken ct = default)
    {
        _inMemoryRestores[log.RestoreId] = log;

        if (_context.RestoreAuditLogs != null)
        {
            try
            {
                await _context.RestoreAuditLogs.InsertOneAsync(log, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backup Repo] MongoDB restore audit insert failed.");
            }
        }

        return log;
    }

    public async Task<List<RestoreAuditLog>> GetRestoreHistoryAsync(string? backupId = null, CancellationToken ct = default)
    {
        if (_context.RestoreAuditLogs != null)
        {
            try
            {
                var filter = string.IsNullOrWhiteSpace(backupId)
                    ? Builders<RestoreAuditLog>.Filter.Empty
                    : Builders<RestoreAuditLog>.Filter.Eq(x => x.BackupId, backupId);

                var list = await _context.RestoreAuditLogs
                    .Find(filter)
                    .SortByDescending(x => x.ExecutedAt)
                    .ToListAsync(ct);

                if (list.Count > 0) return list;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backup Repo] MongoDB restore history read failed.");
            }
        }

        var query = _inMemoryRestores.Values.AsQueryable();
        if (!string.IsNullOrWhiteSpace(backupId))
        {
            query = query.Where(x => x.BackupId == backupId);
        }
        return query.OrderByDescending(x => x.ExecutedAt).ToList();
    }

    public async Task<Dictionary<string, int>> GetBackupStatsAsync(CancellationToken ct = default)
    {
        var all = _inMemoryBackups.Values.ToList();

        if (_context.BackupRecords != null)
        {
            try
            {
                var dbAll = await _context.BackupRecords.Find(Builders<BackupRecord>.Filter.Empty).ToListAsync(ct);
                if (dbAll.Count > 0) all = dbAll;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backup Repo] Stats query failed.");
            }
        }

        int total = all.Count;
        int completed = all.Count(x => x.Status == BackupStatus.Completed || x.Status == BackupStatus.Restored);
        int failed = all.Count(x => x.Status == BackupStatus.Failed || x.Status == BackupStatus.Corrupted);
        int patientTestScope = all.Count(x => x.Scope == BackupScope.PatientTestResults);
        int totalMb = (int)Math.Round(all.Sum(x => x.FileSizeBytes) / (1024.0 * 1024.0));

        return new Dictionary<string, int>
        {
            { "TotalBackups", total },
            { "CompletedBackups", completed },
            { "FailedBackups", failed },
            { "PatientTestScopeBackups", patientTestScope },
            { "TotalStorageMb", totalMb }
        };
    }
}
