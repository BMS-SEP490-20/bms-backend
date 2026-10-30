using BMS.Application.DTOs.Dashboard;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <inheritdoc />
/// <remarks>
/// WHY: server-side aggregation (count / $group / $sum) so no account, invoice or device document leaves the
/// database — the Admin dashboard must never carry clinical or patient data (decision 10).
/// </remarks>
public sealed class AdminOpsDashboardReader(IMongoDatabase database) : IAdminOpsDashboardReader
{
    private readonly IMongoCollection<User> _accounts = database.GetCollection<User>("accounts");
    private readonly IMongoCollection<BackupRecord> _backups = database.GetCollection<BackupRecord>("backup_records");
    private readonly IMongoCollection<BsonDocument> _auditLogs = database.GetCollection<BsonDocument>("audit_logs");
    private readonly IMongoCollection<IotDeviceThreshold> _devices = database.GetCollection<IotDeviceThreshold>("iot_thresholds");
    private readonly IMongoCollection<Invoice> _invoices = database.GetCollection<Invoice>("invoices");

    /// <inheritdoc />
    public async Task<AdminOpsDashboardDto> ReadAsync(DateTime nowUtc, CancellationToken ct)
    {
        return new AdminOpsDashboardDto(
            await UsersPerRoleAsync(ct),
            await LockedAccountsAsync(nowUtc, ct),
            await LoginsAsync(nowUtc, ct),
            await BackupAsync(nowUtc, ct),
            await _auditLogs.EstimatedDocumentCountAsync(cancellationToken: ct),
            await DevicesAsync(ct),
            await RevenueAsync(ct),
            nowUtc);
    }

    private async Task<IReadOnlyDictionary<string, long>> UsersPerRoleAsync(CancellationToken ct)
    {
        var groups = await _accounts.Aggregate()
            .Group(new BsonDocument { { "_id", "$RoleId" }, { "n", new BsonDocument("$sum", 1) } })
            .ToListAsync(ct);
        var result = Enumerable.Range(1, 6).ToDictionary(id => RoleNames.FromRoleId(id)!, _ => 0L);
        foreach (var g in groups)
        {
            // WHY: missing RoleId and legacy 9 are both read as Patient (User.Role), keep the dashboard consistent.
            var id = g["_id"].IsNumeric ? g["_id"].ToInt32() : (int)UserRole.Patient;
            if (id == User.LegacyPatientRoleId) id = (int)UserRole.Patient;
            var name = RoleNames.FromRoleId(id);
            if (name != null) result[name] += g["n"].ToInt64();
        }
        return result;
    }

    private Task<long> LockedAccountsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var f = Builders<User>.Filter;
        var filter = f.Or(f.Eq(u => u.Status, (int)UserStatus.Locked), f.Gt(u => u.LockoutEnd, nowUtc));
        return _accounts.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    private async Task<AdminLoginStatsDto> LoginsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var f = Builders<User>.Filter;
        return new AdminLoginStatsDto(
            await _accounts.CountDocumentsAsync(f.Gte(u => u.LastLoginAt, nowUtc.AddHours(-24)), cancellationToken: ct),
            await _accounts.CountDocumentsAsync(f.Gte(u => u.LastLoginAt, nowUtc.AddDays(-7)), cancellationToken: ct),
            await _accounts.CountDocumentsAsync(f.Gt(u => u.FailedLoginCount, 0), cancellationToken: ct));
    }

    private async Task<AdminBackupStatusDto> BackupAsync(DateTime nowUtc, CancellationToken ct)
    {
        var f = Builders<BackupRecord>.Filter;
        var last = await _backups.Find(f.Empty)
            .SortByDescending(b => b.CreatedAt)
            .Project(b => new { b.Status, b.CreatedAt, b.CompletedAt })
            .FirstOrDefaultAsync(ct);
        var since = nowUtc.AddDays(-7);
        var completed = await _backups.CountDocumentsAsync(
            f.And(f.Gte(b => b.CreatedAt, since), f.Eq(b => b.Status, BackupStatus.Completed)), cancellationToken: ct);
        var failed = await _backups.CountDocumentsAsync(
            f.And(f.Gte(b => b.CreatedAt, since), f.In(b => b.Status, new[] { BackupStatus.Failed, BackupStatus.Corrupted })),
            cancellationToken: ct);
        return new AdminBackupStatusDto(last?.Status.ToString(), last?.CreatedAt, last?.CompletedAt, completed, failed);
    }

    private async Task<AdminIotDeviceStatsDto> DevicesAsync(CancellationToken ct)
    {
        var f = Builders<IotDeviceThreshold>.Filter;
        var total = await _devices.CountDocumentsAsync(f.Empty, cancellationToken: ct);
        var online = await _devices.CountDocumentsAsync(f.In(d => d.Status, new[] { "Active", "Warning" }), cancellationToken: ct);
        return new AdminIotDeviceStatsDto(online, total - online);
    }

    private async Task<AdminRevenueDto> RevenueAsync(CancellationToken ct)
    {
        var rows = await _invoices.Aggregate()
            .Match(Builders<Invoice>.Filter.Eq(i => i.Status, InvoiceStatus.Paid))
            .Group(i => 1, g => new { Total = g.Sum(x => x.TotalAmount), Count = g.LongCount() })
            .ToListAsync(ct);
        var row = rows.FirstOrDefault();
        return new AdminRevenueDto(row?.Total ?? 0m, row?.Count ?? 0);
    }
}
