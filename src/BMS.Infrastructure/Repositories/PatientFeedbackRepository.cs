using System.Text.RegularExpressions;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories
{
    /// <summary>
    /// MongoDB implementation of <see cref="IPatientFeedbackRepository"/> over <c>patient_feedbacks</c> (I3-094).
    /// Replaces the legacy EF Core implementation.
    /// </summary>
    public sealed class PatientFeedbackRepository : IPatientFeedbackRepository
    {
        private const string AllFilterValue = "ALL";
        private const string DeletedStatus = "DELETED";
        private const int MaxResults = 500;
        private readonly IMongoCollection<PatientFeedback> _feedbacks;

        /// <summary>Creates the repository bound to <c>patient_feedbacks</c>.</summary>
        public PatientFeedbackRepository(IMongoDatabase database)
        {
            _feedbacks = database.GetCollection<PatientFeedback>("patient_feedbacks");
        }

        private static FilterDefinitionBuilder<PatientFeedback> F => Builders<PatientFeedback>.Filter;
        private static bool IsSet(string? v) => !string.IsNullOrWhiteSpace(v) && v != AllFilterValue;

        /// <inheritdoc />
        public async Task<IReadOnlyList<PatientFeedback>> GetAllAsync(int? rating, string? category, string? status, string? searchTerm, CancellationToken ct = default)
        {
            var filter = F.Empty;
            if (rating is > 0) filter &= F.Eq(f => f.Rating, rating.Value);
            if (IsSet(category)) filter &= F.Eq(f => f.ServiceCategory, category);
            if (IsSet(status)) filter &= F.Eq(f => f.Status, status);
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var regex = new BsonRegularExpression(Regex.Escape(searchTerm.Trim()), "i");
                filter &= F.Or(F.Regex(f => f.Title, regex), F.Regex(f => f.Comment, regex),
                    F.Regex(f => f.PatientName, regex), F.Regex(f => f.DoctorName, regex));
            }
            return await _feedbacks.Find(filter).SortByDescending(f => f.CreatedAt).Limit(MaxResults).ToListAsync(ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<PatientFeedback>> GetByPatientIdAsync(string patientId, CancellationToken ct = default) =>
            await _feedbacks.Find(F.Eq(f => f.PatientId, patientId)).SortByDescending(f => f.CreatedAt).Limit(MaxResults).ToListAsync(ct);

        /// <inheritdoc />
        public async Task<PatientFeedback?> GetByIdAsync(string id, CancellationToken ct = default) =>
            await _feedbacks.Find(F.Eq(f => f.FeedbackId, id)).FirstOrDefaultAsync(ct);

        /// <inheritdoc />
        public Task AddAsync(PatientFeedback entity, CancellationToken ct = default) =>
            _feedbacks.InsertOneAsync(entity, cancellationToken: ct);

        /// <inheritdoc />
        public async Task<bool> UpdateAsync(PatientFeedback entity, CancellationToken ct = default)
        {
            var result = await _feedbacks.ReplaceOneAsync(F.Eq(f => f.FeedbackId, entity.FeedbackId), entity, cancellationToken: ct);
            return result.MatchedCount > 0;
        }

        /// <inheritdoc />
        public async Task<PatientFeedbackStatistics> GetStatisticsAsync(CancellationToken ct = default)
        {
            var active = F.Ne(f => f.Status, DeletedStatus);
            var byRating = await _feedbacks.Aggregate().Match(active)
                .Group(f => f.Rating, g => new { Rating = g.Key, Count = g.LongCount() })
                .ToListAsync(ct);
            var total = byRating.Sum(r => r.Count);
            var average = total > 0 ? Math.Round(byRating.Sum(r => (double)r.Rating * r.Count) / total, 1) : 5.0;
            var responded = await _feedbacks.CountDocumentsAsync(active & F.Ne(f => f.ManagerResponse, null) & F.Ne(f => f.ManagerResponse, ""), cancellationToken: ct);
            var pending = await _feedbacks.CountDocumentsAsync(F.Eq(f => f.Status, "PENDING"), cancellationToken: ct);
            var distribution = Enumerable.Range(1, 5).Reverse()
                .ToDictionary(star => star, star => byRating.FirstOrDefault(r => r.Rating == star)?.Count ?? 0);
            return new PatientFeedbackStatistics(total, average, responded, pending, distribution);
        }
    }
}
