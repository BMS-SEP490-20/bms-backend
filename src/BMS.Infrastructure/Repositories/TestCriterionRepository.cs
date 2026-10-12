using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Mongo;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

public sealed class TestCriterionRepository : ITestCriterionRepository
{
    private readonly IMongoCollection<TestCriterion> _col;

    public TestCriterionRepository(IMongoDatabase db, MongoCollections names)
        => _col = db.GetCollection<TestCriterion>(names.TestCriteria);

    public async Task<List<TestCriterion>> GetAllAsync(CancellationToken ct = default)
        => await _col.Find(FilterDefinition<TestCriterion>.Empty)
            .SortBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
}