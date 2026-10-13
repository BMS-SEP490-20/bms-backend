using BMS.Application.Interfaces;
using BMS.Core.Entities;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// MongoDB repository for the <c>test_packages</c> collection.
public class TestPackageRepository : ITestPackageRepository
{
    private readonly IMongoCollection<TestPackage> _collection;

    /// Initializes the repository with the injected MongoDB database.
    /// <param name="database">IMongoDatabase resolved from DI.</param>
    public TestPackageRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<TestPackage>("test_packages");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TestPackage>> GetActivePackagesAsync(
        string? category = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<TestPackage>.Filter;
        var filter = filterBuilder.Eq(p => p.IsActive, true);

        if (!string.IsNullOrWhiteSpace(category))
        {
            filter &= filterBuilder.Eq(p => p.Category, category.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            // Case-insensitive match on package_name or package_code
            var searchFilter = filterBuilder.Or(
                filterBuilder.Regex(p => p.PackageName, new MongoDB.Bson.BsonRegularExpression(keyword, "i")),
                filterBuilder.Regex(p => p.PackageCode, new MongoDB.Bson.BsonRegularExpression(keyword, "i"))
            );
            filter &= searchFilter;
        }

        var result = await _collection
            .Find(filter)
            .SortBy(p => p.Category)
            .ThenBy(p => p.PackageName)
            .ToListAsync(cancellationToken);

        return result;
    }

    /// <inheritdoc />
    public async Task<TestPackage?> GetActiveByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<TestPackage>.Filter.And(
            Builders<TestPackage>.Filter.Eq(p => p.Id, id),
            Builders<TestPackage>.Filter.Eq(p => p.IsActive, true)
        );

        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }
}