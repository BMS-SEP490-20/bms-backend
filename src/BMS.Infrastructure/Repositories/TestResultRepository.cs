using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories
{
    /// <summary>
    /// MongoDB implementation of <see cref="ITestResultRepository"/> on <c>test_results</c>.
    /// Replaces the legacy EF Core repository; shares the collection and mapping used by
    /// ResultRepository (enter results) and TestReportService.
    /// </summary>
    public sealed class TestResultRepository : ITestResultRepository
    {
        private readonly IMongoCollection<TestResult> _results;

        /// <summary>Creates the repository bound to <c>test_results</c>.</summary>
        public TestResultRepository(IMongoDatabase database)
        {
            _results = database.GetCollection<TestResult>("test_results");
        }

        private static FilterDefinitionBuilder<TestResult> F => Builders<TestResult>.Filter;
        private static FilterDefinition<TestResult> Active => F.Ne(x => x.IsDeleted, true);
        private static FilterDefinition<TestResult> ById(string id) => F.Or(F.Eq(x => x.Id, id), F.Eq(x => x.TestResultId, id)) & Active;

        /// <inheritdoc />
        public async Task<(IReadOnlyList<TestResult> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
        {
            var total = await _results.CountDocumentsAsync(Active, cancellationToken: ct);
            var items = await _results.Find(Active).SortByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);
            return (items, total);
        }

        /// <inheritdoc />
        public async Task<TestResult?> GetByIdAsync(string id, CancellationToken ct = default) =>
            await _results.Find(ById(id)).FirstOrDefaultAsync(ct);

        /// <inheritdoc />
        public Task AddAsync(TestResult entity, CancellationToken ct = default) =>
            _results.InsertOneAsync(entity, cancellationToken: ct);

        /// <inheritdoc />
        public async Task<bool> UpdateFieldsAsync(string id, TestResultFieldChanges c, CancellationToken ct = default)
        {
            var u = Builders<TestResult>.Update;
            var updates = new List<UpdateDefinition<TestResult>> { u.Set(x => x.UpdatedAt, DateTime.UtcNow) };
            if (c.ReagentCode is not null) updates.Add(u.Set(x => x.ReagentCode, c.ReagentCode));
            if (c.Quantity is not null) updates.Add(u.Set(x => x.Quantity, c.Quantity));
            if (c.DeviceId is not null) updates.Add(u.Set(x => x.DeviceId, c.DeviceId));
            if (c.ReviewedBy is not null) updates.Add(u.Set(x => x.ReviewedBy, c.ReviewedBy));
            if (c.ReviewedAt is not null) updates.Add(u.Set(x => x.ReviewedAt, c.ReviewedAt));
            if (c.Status is not null) updates.Add(u.Set(x => x.Status, c.Status));
            if (c.Items is not null) updates.Add(u.Set(x => x.Items, c.Items));
            if (c.ValidationStatus is not null) updates.Add(u.Set(x => x.ValidationStatus, c.ValidationStatus.Value));
            if (c.ApprovedByDoctorId is not null) updates.Add(u.Set(x => x.ApprovedByDoctorId, c.ApprovedByDoctorId));
            if (c.ApprovedAt is not null) updates.Add(u.Set(x => x.ApprovedAt, c.ApprovedAt));

            var result = await _results.UpdateOneAsync(ById(id), u.Combine(updates), cancellationToken: ct);
            return result.MatchedCount > 0;
        }

        /// <inheritdoc />
        public async Task<bool> SoftDeleteAsync(string id, CancellationToken ct = default)
        {
            var result = await _results.UpdateOneAsync(ById(id), Builders<TestResult>.Update
                .Set(x => x.IsDeleted, true).Set(x => x.UpdatedAt, DateTime.UtcNow), cancellationToken: ct);
            return result.MatchedCount > 0;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TestResult>> GetByAppointmentIdAsync(string appointmentId, CancellationToken ct = default) =>
            await _results.Find(F.Eq(x => x.AppointmentId, appointmentId) & Active).SortBy(x => x.CreatedAt).Limit(200).ToListAsync(ct);
    }
}
