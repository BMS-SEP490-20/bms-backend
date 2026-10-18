using System.Text.RegularExpressions;
using BMS.Application.DTOs;
using BMS.Application.DTOs.Common;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// MongoDB repository for the <c>test_packages</c> collection.
public class TestPackageRepository : ITestPackageRepository
{
    private readonly IMongoCollection<TestPackage> _collection;
    private readonly IMongoCollection<BsonDocument> _mapCollection;
    private readonly IMongoCollection<BsonDocument> _criteriaCollection;

    /// Initializes the repository with the injected MongoDB database.
    /// <param name="database">IMongoDatabase resolved from DI.</param>
    public TestPackageRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<TestPackage>("test_packages");
        _mapCollection = database.GetCollection<BsonDocument>("test_package_criteria");
        _criteriaCollection = database.GetCollection<BsonDocument>("test_criteria");
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
            var catRx = new BsonRegularExpression($"^{Regex.Escape(category.Trim())}$", "i");
            filter &= filterBuilder.Regex(p => p.Category, catRx);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            var rx = new BsonRegularExpression(Regex.Escape(keyword), "i");
            var matchedIdsByCriteria = await FindPackageIdsMatchingCriterionAsync(rx, cancellationToken);

            var searchFilter = filterBuilder.Or(
                filterBuilder.Regex(p => p.PackageName, rx),
                filterBuilder.Regex(p => p.PackageCode, rx),
                filterBuilder.Regex(p => p.Description, rx),
                filterBuilder.Regex(p => p.Category, rx),
                filterBuilder.Regex(p => p.SampleTube, rx),
                filterBuilder.In(p => p.Id, matchedIdsByCriteria)
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
    public Task<TestPackage?> GetActiveByIdAsync(string id, CancellationToken cancellationToken = default)
        => GetByIdOrCodeAsync(id, includeInactive: false, cancellationToken);

    /// <inheritdoc />
    public async Task<TestPackage?> GetByIdOrCodeAsync(
        string idOrCode,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            return null;

        var trimmed = idOrCode.Trim();
        var fb = Builders<TestPackage>.Filter;
        var filter = fb.Or(
            fb.Eq(p => p.Id, trimmed),
            fb.Eq(p => p.PackageCode, trimmed.ToUpperInvariant()));

        if (!includeInactive)
        {
            filter = fb.And(filter, fb.Eq(p => p.IsActive, true));
        }

        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResult<TestPackage>> SearchPackagesAsync(
        TestPackageSearchQuery q,
        CancellationToken cancellationToken = default)
    {
        var fb = Builders<TestPackage>.Filter;
        var filters = new List<FilterDefinition<TestPackage>>();

        if (q.IsActive is { } active)
        {
            filters.Add(fb.Eq(p => p.IsActive, active));
        }

        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            var catRx = new BsonRegularExpression($"^{Regex.Escape(q.Category.Trim())}$", "i");
            filters.Add(fb.Regex(p => p.Category, catRx));
        }

        if (!string.IsNullOrWhiteSpace(q.SampleTube))
        {
            var tubeRx = new BsonRegularExpression(Regex.Escape(q.SampleTube.Trim()), "i");
            filters.Add(fb.Regex(p => p.SampleTube, tubeRx));
        }

        if (q.MinPrice is { } min)
        {
            filters.Add(fb.Gte(p => p.Price, min));
        }

        if (q.MaxPrice is { } max)
        {
            filters.Add(fb.Lte(p => p.Price, max));
        }

        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            var rx = new BsonRegularExpression(Regex.Escape(q.Keyword.Trim()), "i");
            var matchedIdsByCriteria = await FindPackageIdsMatchingCriterionAsync(rx, cancellationToken);

            filters.Add(fb.Or(
                fb.Regex(p => p.PackageCode, rx),
                fb.Regex(p => p.PackageName, rx),
                fb.Regex(p => p.Description, rx),
                fb.Regex(p => p.Category, rx),
                fb.Regex(p => p.SampleTube, rx),
                fb.In(p => p.Id, matchedIdsByCriteria)));
        }

        var finalFilter = filters.Count > 0 ? fb.And(filters) : FilterDefinition<TestPackage>.Empty;
        var total = await _collection.CountDocumentsAsync(finalFilter, cancellationToken: cancellationToken);

        var items = await _collection
            .Find(finalFilter)
            .Sort(BuildSort(q))
            .Skip((q.Page - 1) * q.PageSize)
            .Limit(q.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TestPackage>(items, q.Page, q.PageSize, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<TestPackageCriterionSummaryDto>>> GetCriteriaByPackageIdsAsync(
        IEnumerable<string> packageIds,
        CancellationToken cancellationToken = default)
    {
        var ids = packageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var result = new Dictionary<string, IReadOnlyList<TestPackageCriterionSummaryDto>>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return result;

        try
        {
            var maps = await _mapCollection
                .Find(Builders<BsonDocument>.Filter.In("package_id", ids))
                .ToListAsync(cancellationToken);

            if (maps.Count == 0)
                return result;

            var criterionIds = maps
                .Select(m => m.GetValue("criteria_id", BsonNull.Value).ToString() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var criteriaDocs = await _criteriaCollection
                .Find(Builders<BsonDocument>.Filter.In("_id", criterionIds.Select(id => (BsonValue)id)))
                .ToListAsync(cancellationToken);

            var criteriaById = criteriaDocs.ToDictionary(
                c => c.GetValue("_id", BsonNull.Value).ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

            foreach (var group in maps.GroupBy(m => m.GetValue("package_id", BsonNull.Value).ToString() ?? string.Empty))
            {
                if (string.IsNullOrWhiteSpace(group.Key)) continue;
                var list = new List<TestPackageCriterionSummaryDto>();
                var idx = 1;
                foreach (var map in group)
                {
                    var critId = map.GetValue("criteria_id", BsonNull.Value).ToString() ?? string.Empty;
                    var displayOrder = map.Contains("display_order") && map["display_order"].IsInt32
                        ? map["display_order"].AsInt32
                        : idx++;

                    if (criteriaById.TryGetValue(critId, out var crit))
                    {
                        var code = crit.Contains("code") ? crit["code"].AsString :
                                   crit.Contains("criterionCode") ? crit["criterionCode"].AsString : string.Empty;
                        var name = crit.Contains("name") ? crit["name"].AsString :
                                   crit.Contains("criterionName") ? crit["criterionName"].AsString : string.Empty;
                        var unit = crit.Contains("unit") && !crit["unit"].IsBsonNull ? crit["unit"].AsString : string.Empty;

                        list.Add(new TestPackageCriterionSummaryDto
                        {
                            CriterionId = critId,
                            Code = code,
                            Name = name,
                            Unit = unit,
                            DisplayOrder = displayOrder
                        });
                    }
                }
                result[group.Key] = list.OrderBy(x => x.DisplayOrder).ToList();
            }
        }
        catch
        {
            // Non-fatal if criteria mapping collections are empty
        }

        return result;
    }

    private async Task<List<string>> FindPackageIdsMatchingCriterionAsync(
        BsonRegularExpression rx,
        CancellationToken ct)
    {
        try
        {
            var cf = Builders<BsonDocument>.Filter;
            var matchingCriteria = await _criteriaCollection
                .Find(cf.Or(
                    cf.Regex("code", rx),
                    cf.Regex("name", rx),
                    cf.Regex("criterionCode", rx),
                    cf.Regex("criterionName", rx)))
                .ToListAsync(ct);

            if (matchingCriteria.Count == 0)
                return [];

            var critIds = matchingCriteria
                .Select(c => c.GetValue("_id", BsonNull.Value).ToString() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            var maps = await _mapCollection
                .Find(Builders<BsonDocument>.Filter.In("criteria_id", critIds))
                .ToListAsync(ct);

            return maps
                .Select(m => m.GetValue("package_id", BsonNull.Value).ToString() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static SortDefinition<TestPackage> BuildSort(TestPackageSearchQuery q)
    {
        var sb = Builders<TestPackage>.Sort;
        SortDefinition<TestPackage> primary = q.SortBy switch
        {
            "name" => q.SortDesc ? sb.Descending(p => p.PackageName) : sb.Ascending(p => p.PackageName),
            "code" => q.SortDesc ? sb.Descending(p => p.PackageCode) : sb.Ascending(p => p.PackageCode),
            "price" => q.SortDesc ? sb.Descending(p => p.Price) : sb.Ascending(p => p.Price),
            _ => q.SortDesc ? sb.Descending(p => p.Category) : sb.Ascending(p => p.Category)
        };
        return sb.Combine(primary, sb.Ascending(p => p.PackageName), sb.Ascending(p => p.Id));
    }
}