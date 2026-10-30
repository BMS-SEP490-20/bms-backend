using System.Text.RegularExpressions;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories
{
    /// <summary>
    /// MongoDB implementation of <see cref="IAuditLogRepository"/> over the <c>audit_logs</c> collection (I3-049).
    /// Replaces the former EF Core <c>TestOrderManagementDbContext</c> implementation, which does not exist in BMS.
    /// </summary>
    public sealed class AuditLogRepository : IAuditLogRepository
    {
        /// <summary>Collection name shared with UC76 Test History audit entries.</summary>
        public const string CollectionName = "audit_logs";

        private const string AllFilterValue = "ALL";
        private readonly IMongoCollection<AuditLog> _logs;

        /// <summary>Creates the repository bound to the <c>audit_logs</c> collection.</summary>
        public AuditLogRepository(IMongoDatabase database)
        {
            _logs = database.GetCollection<AuditLog>(CollectionName);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<AuditLog>> GetAllAsync(AuditLogQuery filter, CancellationToken ct = default)
        {
            var pageSize = Math.Clamp(filter.PageSize, 1, 200);
            var skip = (Math.Max(1, filter.PageNumber) - 1) * pageSize;
            return await _logs.Find(BuildFilter(filter))
                .SortByDescending(a => a.Timestamp)
                .Skip(skip)
                .Limit(pageSize)
                .ToListAsync(ct);
        }

        /// <inheritdoc />
        public Task<long> GetCountAsync(AuditLogQuery filter, CancellationToken ct = default) =>
            _logs.CountDocumentsAsync(BuildFilter(filter), cancellationToken: ct);

        /// <inheritdoc />
        public async Task<AuditLog?> GetByIdAsync(string id, CancellationToken ct = default) =>
            await _logs.Find(Builders<AuditLog>.Filter.Eq(a => a.AuditId, id)).FirstOrDefaultAsync(ct);

        /// <inheritdoc />
        public Task AddAsync(AuditLog entity, CancellationToken ct = default) =>
            _logs.InsertOneAsync(entity, cancellationToken: ct);

        /// <inheritdoc />
        public async Task<AuditLogStatistics> GetStatisticsAsync(CancellationToken ct = default)
        {
            var f = Builders<AuditLog>.Filter;
            var total = await _logs.CountDocumentsAsync(f.Empty, cancellationToken: ct);
            var security = await _logs.CountDocumentsAsync(f.Eq(x => x.Category, "SECURITY"), cancellationToken: ct);
            var access = await _logs.CountDocumentsAsync(f.Eq(x => x.Category, "ACCESS"), cancellationToken: ct);
            var critical = await _logs.CountDocumentsAsync(f.In(x => x.Severity, new[] { "CRITICAL", "ERROR" }), cancellationToken: ct);
            var failed = await _logs.CountDocumentsAsync(f.Eq(x => x.Status, "FAILED"), cancellationToken: ct);
            return new AuditLogStatistics(total, security, access, critical, failed);
        }

        private static FilterDefinition<AuditLog> BuildFilter(AuditLogQuery q)
        {
            var f = Builders<AuditLog>.Filter;
            var filter = f.Empty;
            if (IsSet(q.Category)) filter &= f.Eq(a => a.Category, q.Category);
            if (IsSet(q.Severity)) filter &= f.Eq(a => a.Severity, q.Severity);
            if (IsSet(q.Status)) filter &= f.Eq(a => a.Status, q.Status);
            if (IsSet(q.Action)) filter &= f.Eq(a => a.Action, q.Action);
            if (!string.IsNullOrWhiteSpace(q.SearchTerm))
            {
                // Escape user input so it is matched literally, never as a regex supplied by the caller.
                var regex = new BsonRegularExpression(Regex.Escape(q.SearchTerm.Trim()), "i");
                filter &= f.Or(f.Regex(a => a.UserName, regex), f.Regex(a => a.Action, regex),
                    f.Regex(a => a.Details, regex), f.Regex(a => a.TargetEntity, regex));
            }
            if (q.FromDate.HasValue) filter &= f.Gte(a => a.Timestamp, q.FromDate.Value);
            if (q.ToDate.HasValue) filter &= f.Lte(a => a.Timestamp, q.ToDate.Value);
            return filter;
        }

        private static bool IsSet(string? value) => !string.IsNullOrWhiteSpace(value) && value != AllFilterValue;
    }
}
