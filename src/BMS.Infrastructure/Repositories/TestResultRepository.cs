using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;
using MongoDB.Bson;

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
        private readonly IMongoCollection<BsonDocument> _parameters;

        /// <summary>Creates the repository bound to <c>test_results</c>.</summary>
        public TestResultRepository(IMongoDatabase database)
        {
            _results = database.GetCollection<TestResult>("test_results");
            _parameters = database.GetCollection<BsonDocument>("test_parameters");
        }

        private static FilterDefinitionBuilder<TestResult> F => Builders<TestResult>.Filter;
        private static FilterDefinition<TestResult> Active => F.Ne(x => x.IsDeleted, true);
        // WHY in the filter: makes "signed = read-only" atomic; an edit racing an approval cannot land after the signature.
        private static FilterDefinition<TestResult> NotSigned => F.Not(Signed);
        private static FilterDefinition<TestResult> Signed =>
            F.Or(F.Eq(x => x.ValidationStatus, ResultValidationStatus.DoctorApproved), F.Ne(x => x.ApprovedAt, null),
                F.Regex(x => x.Status, new BsonRegularExpression("^APPROVED$", "i")),
                new BsonDocument("approved_at", new BsonDocument("$type", "date")),
                new BsonDocument("digital_signature", new BsonDocument { { "$type", "string" }, { "$regex", @"\S" } }),
                new BsonDocument("digitalSignature", new BsonDocument { { "$type", "string" }, { "$regex", @"\S" } }));
        private static FilterDefinition<TestResult> ById(string id) => F.Or(F.Eq(x => x.Id, id), F.Eq(x => x.TestResultId, id)) & Active;

        /// <inheritdoc />
        public async Task<(IReadOnlyList<TestResult> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
        {
            var total = await _results.CountDocumentsAsync(Active, cancellationToken: ct);
            var items = await _results.Find(Active).SortByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync(ct);
            await HydrateLegacyParametersAsync(items, ct);
            return (items, total);
        }

        /// <inheritdoc />
        public async Task<TestResult?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var result = await _results.Find(ById(id)).FirstOrDefaultAsync(ct);
            if (result is not null) await HydrateLegacyParametersAsync([result], ct);
            return result;
        }

        /// <summary>Reads legacy measurements from their original collection without rewriting patient records.</summary>
        private async Task HydrateLegacyParametersAsync(IEnumerable<TestResult> results, CancellationToken ct)
        {
            var missing = results.Where(x => x.Items.Count == 0).ToList();
            if (missing.Count == 0) return;
            // Batch all measurements without a global row limit that could truncate later results.
            var ids = new BsonArray(missing.Select(x => x.Id).Distinct());
            var parameters = await _parameters.Find(new BsonDocument("result_id",
                new BsonDocument("$in", ids))).ToListAsync(ct);
            static string Text(BsonDocument doc, string key) => doc.TryGetValue(key, out var v) && !v.IsBsonNull ? v.ToString() ?? "" : "";
            var byResult = parameters.ToLookup(p => Text(p, "result_id"));
            foreach (var result in missing)
            {
                result.Items = byResult[result.Id].Select(p => new TestResultItem
                {
                    CriterionId = Text(p, "criterion_id"), CriterionCode = Text(p, "code"), CriterionName = Text(p, "name"),
                    MeasuredValue = Text(p, "value_numeric") is { Length: > 0 } number ? number : Text(p, "value_text"),
                    Unit = Text(p, "unit"), ReferenceInterval = Text(p, "reference_display"),
                    OriginalFlag = Text(p, "flag"),
                    Flag = Text(p, "flag").Trim().ToUpperInvariant().Replace("_", "").Replace("-", "").Replace(" ", "") switch
                    {
                        "N" or "NORMAL" or "0" => ResultFlag.Normal,
                        "H" or "HIGH" or "2" => ResultFlag.High,
                        "L" or "LOW" or "1" => ResultFlag.Low,
                        "HH" or "LL" or "C" or "CRITICAL" or "CRITICALPANIC" or "CRITICALHIGH" or "CRITICALLOW"
                            or "PANIC" or "PANICHIGH" or "PANICLOW" or "3" => ResultFlag.CriticalPanic,
                        // Unknown and missing codes require review; neither implies normality.
                        _ => ResultFlag.Unknown
                    }
                }).ToList();
                result.IsPanic |= result.Items.Any(x => x.Flag == ResultFlag.CriticalPanic);
            }
        }

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
            if (c.IsPanic.HasValue) updates.Add(u.Set(x => x.IsPanic, c.IsPanic.Value));

            var result = await _results.UpdateOneAsync(ById(id) & NotSigned, u.Combine(updates), cancellationToken: ct);
            return result.MatchedCount > 0;
        }

        /// <inheritdoc />
        public async Task<bool> SoftDeleteAsync(string id, CancellationToken ct = default)
        {
            var result = await _results.UpdateOneAsync(ById(id) & NotSigned, Builders<TestResult>.Update
                .Set(x => x.IsDeleted, true).Set(x => x.UpdatedAt, DateTime.UtcNow), cancellationToken: ct);
            return result.MatchedCount > 0;
        }

        /// <inheritdoc />
        public async Task<bool> AnySignedForOrderAsync(IReadOnlyCollection<string> sampleKeys, IReadOnlyCollection<string> orderKeys,
            CancellationToken ct = default)
        {
            var link = F.Or(F.In(x => x.SampleId, sampleKeys), F.In(x => x.AppointmentId, orderKeys),
                new BsonDocument("sample_id", new BsonDocument("$in", new BsonArray(sampleKeys))),
                new BsonDocument("appointment_id", new BsonDocument("$in", new BsonArray(orderKeys))));
            return await _results.Find(link & Active & Signed).Limit(1).AnyAsync(ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TestResult>> GetByAppointmentIdAsync(string appointmentId, CancellationToken ct = default)
        {
            var link = F.Or(F.Eq(x => x.AppointmentId, appointmentId), new BsonDocument("appointment_id", appointmentId));
            var results = await _results.Find(link & Active).SortBy(x => x.CreatedAt).Limit(200).ToListAsync(ct);
            await HydrateLegacyParametersAsync(results, ct);
            return results;
        }
    }
}
