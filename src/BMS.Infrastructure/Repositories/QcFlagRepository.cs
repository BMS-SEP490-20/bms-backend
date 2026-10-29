using System.Text.RegularExpressions;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories
{
    /// <summary>
    /// MongoDB implementation of <see cref="IQcFlagRepository"/> over <c>qc_flags</c> (I3-076).
    /// Replaces the legacy EF Core implementation.
    /// </summary>
    public sealed class QcFlagRepository : IQcFlagRepository
    {
        private const string AllFilterValue = "ALL";
        private const int MaxResults = 500;
        private readonly IMongoCollection<QcFlag> _flags;

        /// <summary>Creates the repository bound to <c>qc_flags</c>.</summary>
        public QcFlagRepository(IMongoDatabase database)
        {
            _flags = database.GetCollection<QcFlag>("qc_flags");
        }

        private static FilterDefinitionBuilder<QcFlag> F => Builders<QcFlag>.Filter;
        private static bool IsSet(string? v) => !string.IsNullOrWhiteSpace(v) && v != AllFilterValue;

        /// <inheritdoc />
        public async Task<IReadOnlyList<QcFlag>> GetAllAsync(string? status, string? severity, string? analyzerId, string? parameter, CancellationToken ct = default)
        {
            var filter = F.Empty;
            if (IsSet(status)) filter &= F.Eq(q => q.Status, status);
            if (IsSet(severity)) filter &= F.Eq(q => q.Severity, severity);
            if (IsSet(analyzerId)) filter &= F.Eq(q => q.AnalyzerId, analyzerId);
            if (IsSet(parameter))
                filter &= F.Regex(q => q.Parameter, new BsonRegularExpression(Regex.Escape(parameter!.Trim()), "i"));

            return await _flags.Find(filter).SortByDescending(q => q.TriggeredAt).Limit(MaxResults).ToListAsync(ct);
        }

        /// <inheritdoc />
        public async Task<QcFlag?> GetByIdAsync(string id, CancellationToken ct = default) =>
            await _flags.Find(F.Eq(q => q.FlagId, id)).FirstOrDefaultAsync(ct);

        /// <inheritdoc />
        public Task AddAsync(QcFlag entity, CancellationToken ct = default) =>
            _flags.InsertOneAsync(entity, cancellationToken: ct);

        /// <inheritdoc />
        public async Task<bool> UpdateAsync(QcFlag entity, CancellationToken ct = default)
        {
            var result = await _flags.ReplaceOneAsync(F.Eq(q => q.FlagId, entity.FlagId), entity, cancellationToken: ct);
            return result.MatchedCount > 0;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            var result = await _flags.DeleteOneAsync(F.Eq(q => q.FlagId, id), ct);
            return result.DeletedCount > 0;
        }

        /// <inheritdoc />
        public Task<long> CountAsync(string? status = null, string? severity = null, CancellationToken ct = default)
        {
            var filter = F.Empty;
            if (status is not null) filter &= F.Eq(q => q.Status, status);
            if (severity is not null) filter &= F.Eq(q => q.Severity, severity);
            return _flags.CountDocumentsAsync(filter, cancellationToken: ct);
        }
    }
}
