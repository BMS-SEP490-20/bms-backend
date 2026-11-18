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
    private readonly IMongoCollection<ServicePackage> _servicePackagesCollection;
    private readonly IMongoCollection<BsonDocument> _mapCollection;
    private readonly IMongoCollection<BsonDocument> _criteriaCollection;

    /// Initializes the repository with the injected MongoDB database.
    /// <param name="database">IMongoDatabase resolved from DI.</param>
    public TestPackageRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<TestPackage>("test_packages");
        _servicePackagesCollection = database.GetCollection<ServicePackage>("service_packages");
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

        // Synchronize with Admin-managed service_packages (Quản Lý Danh Mục Gói Dịch Vụ & Bảng Giá Viện Phí):
        // Admin's price, name, category, preparation notes, active status, and newly created packages take priority.
        try
        {
            var allServicePackages = await _servicePackagesCollection
                .Find(FilterDefinition<ServicePackage>.Empty)
                .ToListAsync(cancellationToken);

            var activeServicePackages = allServicePackages
                .Where(sp => !sp.IsDeleted && sp.IsActive && !string.IsNullOrWhiteSpace(sp.Name))
                .ToList();

            var activeById = new Dictionary<string, ServicePackage>(StringComparer.OrdinalIgnoreCase);
            var activeByCode = new Dictionary<string, ServicePackage>(StringComparer.OrdinalIgnoreCase);
            var activeByName = new Dictionary<string, ServicePackage>(StringComparer.OrdinalIgnoreCase);
            foreach (var sp in activeServicePackages)
            {
                if (!string.IsNullOrWhiteSpace(sp.Id)) activeById[sp.Id.Trim()] = sp;
                if (!string.IsNullOrWhiteSpace(sp.Code)) activeByCode[sp.Code.Trim()] = sp;
                activeByName[sp.Name.Trim()] = sp;
            }

            var blockedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var blockedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sp in allServicePackages.Where(sp => sp.IsDeleted || !sp.IsActive))
            {
                if (!string.IsNullOrWhiteSpace(sp.Id) && !activeById.ContainsKey(sp.Id.Trim()))
                    blockedIds.Add(sp.Id.Trim());
                if (!string.IsNullOrWhiteSpace(sp.Code) && !activeByCode.ContainsKey(sp.Code.Trim()))
                    blockedCodes.Add(sp.Code.Trim());
            }

            // Exclude stale test_packages that Admin deactivated or soft-deleted in service_packages.
            result.RemoveAll(tp =>
                (!string.IsNullOrWhiteSpace(tp.Id) && blockedIds.Contains(tp.Id.Trim())) ||
                (!string.IsNullOrWhiteSpace(tp.PackageCode) && blockedCodes.Contains(tp.PackageCode.Trim())));

            // Overlay latest Admin metadata/price onto matching test_packages entries.
            foreach (var tp in result)
            {
                ServicePackage? sp = null;
                if (!string.IsNullOrWhiteSpace(tp.Id)) activeById.TryGetValue(tp.Id.Trim(), out sp);
                if (sp is null && !string.IsNullOrWhiteSpace(tp.PackageCode)) activeByCode.TryGetValue(tp.PackageCode.Trim(), out sp);
                if (sp is null && !string.IsNullOrWhiteSpace(tp.PackageName)) activeByName.TryGetValue(tp.PackageName.Trim(), out sp);
                if (sp is null) continue;

                tp.PackageName = sp.Name.Trim();
                tp.Price = sp.DiscountPrice ?? sp.Price;
                if (!string.IsNullOrWhiteSpace(sp.Description)) tp.Description = sp.Description.Trim();
                if (!string.IsNullOrWhiteSpace(sp.Category)) tp.Category = sp.Category.Trim();
                if (!string.IsNullOrWhiteSpace(sp.SampleTube)) tp.SampleTube = sp.SampleTube.Trim();
                if (sp.SampleVolumeMl is > 0) tp.SampleVolumeMl = sp.SampleVolumeMl.Value;
                if (!string.IsNullOrWhiteSpace(sp.Tat)) tp.Tat = sp.Tat.Trim();
                if (!string.IsNullOrWhiteSpace(sp.PreparationNotes)) tp.FastingRule = sp.PreparationNotes.Trim();
                if (!string.IsNullOrWhiteSpace(sp.TargetAudience)) tp.TargetAudience = sp.TargetAudience.Trim();
            }

            var existingCodes = new HashSet<string>(
                result.Select(p => p.PackageCode).Where(c => !string.IsNullOrWhiteSpace(c)),
                StringComparer.OrdinalIgnoreCase);
            var existingIds = new HashSet<string>(
                result.Select(p => p.Id).Where(i => !string.IsNullOrWhiteSpace(i)),
                StringComparer.OrdinalIgnoreCase);
            var existingNames = new HashSet<string>(
                result.Select(p => p.PackageName).Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var sp in activeServicePackages)
            {
                var code = !string.IsNullOrWhiteSpace(sp.Code) ? sp.Code.Trim() : sp.Id;
                if (existingIds.Contains(sp.Id) || existingCodes.Contains(code) || existingNames.Contains(sp.Name.Trim()))
                    continue;

                var cat = !string.IsNullOrWhiteSpace(sp.Category) ? sp.Category.Trim() : "Tổng quát";
                if (!string.IsNullOrWhiteSpace(category) && !cat.Equals(category.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var kw = search.Trim();
                    var matchesText =
                        sp.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        code.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        (sp.Description?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        cat.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                        (sp.Tests?.Any(t =>
                            t.Code.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                            t.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)) ?? false);
                    if (!matchesText) continue;
                }

                result.Add(new TestPackage
                {
                    Id = sp.Id,
                    PackageCode = code,
                    PackageName = sp.Name.Trim(),
                    Category = cat,
                    Price = sp.DiscountPrice ?? sp.Price,
                    Description = sp.Description ?? string.Empty,
                    SampleTube = !string.IsNullOrWhiteSpace(sp.SampleTube) ? sp.SampleTube.Trim() : "EDTA / Serum",
                    SampleVolumeMl = sp.SampleVolumeMl is > 0 ? sp.SampleVolumeMl.Value : 3.0,
                    Tat = sp.Tat,
                    FastingRule = sp.PreparationNotes,
                    TargetAudience = sp.TargetAudience,
                    IsActive = sp.IsActive
                });
                existingIds.Add(sp.Id);
                existingCodes.Add(code);
                existingNames.Add(sp.Name.Trim());
            }
        }
        catch
        {
            await ApplyListedPricesAsync(result, cancellationToken);
        }

        return result
            .OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        var upper = trimmed.ToUpperInvariant();

        // 1. Check Admin-managed service_packages first so edits/deactivations take immediate effect.
        try
        {
            var sfb = Builders<ServicePackage>.Filter;
            var sFilter = sfb.Or(
                sfb.Eq(p => p.Id, trimmed),
                sfb.Eq(p => p.Code, upper));

            var spCandidates = await _servicePackagesCollection.Find(sFilter).ToListAsync(cancellationToken);
            var sp = spCandidates.FirstOrDefault(p => !p.IsDeleted && p.IsActive)
                  ?? spCandidates.FirstOrDefault(p => !p.IsDeleted)
                  ?? spCandidates.FirstOrDefault();

            if (sp is not null)
            {
                if (!includeInactive && (sp.IsDeleted || !sp.IsActive))
                    return null;

                TestPackage? legacy = null;
                try
                {
                    var lfb = Builders<TestPackage>.Filter;
                    legacy = await _collection.Find(lfb.Or(
                        lfb.Eq(p => p.Id, sp.Id),
                        lfb.Eq(p => p.PackageCode, sp.Code.ToUpperInvariant()))).FirstOrDefaultAsync(cancellationToken);
                }
                catch { }

                return new TestPackage
                {
                    Id = sp.Id,
                    PackageCode = sp.Code,
                    PackageName = sp.Name,
                    Category = !string.IsNullOrWhiteSpace(sp.Category) ? sp.Category.Trim() : (legacy?.Category ?? "Tổng quát"),
                    Price = sp.DiscountPrice ?? sp.Price,
                    Description = !string.IsNullOrWhiteSpace(sp.Description) ? sp.Description.Trim() : (legacy?.Description ?? string.Empty),
                    SampleTube = !string.IsNullOrWhiteSpace(sp.SampleTube) ? sp.SampleTube.Trim() : (legacy?.SampleTube ?? "Serum / EDTA"),
                    SampleVolumeMl = sp.SampleVolumeMl is > 0 ? sp.SampleVolumeMl.Value : (legacy?.SampleVolumeMl is > 0 ? legacy.SampleVolumeMl : 2.0),
                    Tat = !string.IsNullOrWhiteSpace(sp.Tat) ? sp.Tat.Trim() : legacy?.Tat,
                    FastingRule = !string.IsNullOrWhiteSpace(sp.PreparationNotes) ? sp.PreparationNotes.Trim() : legacy?.FastingRule,
                    TargetAudience = !string.IsNullOrWhiteSpace(sp.TargetAudience) ? sp.TargetAudience.Trim() : legacy?.TargetAudience,
                    IsActive = sp.IsActive && !sp.IsDeleted
                };
            }
        }
        catch
        {
            // Fall through to test_packages
        }

        // 2. Fallback to test_packages collection.
        var fb = Builders<TestPackage>.Filter;
        var filter = fb.Or(
            fb.Eq(p => p.Id, trimmed),
            fb.Eq(p => p.PackageCode, upper));

        if (!includeInactive)
        {
            filter = fb.And(filter, fb.Eq(p => p.IsActive, true));
        }

        var found = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (found is not null)
        {
            await ApplyListedPricesAsync([found], cancellationToken);
            return found;
        }

        return null;
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

        await ApplyListedPricesAsync(items, cancellationToken);
        return new PagedResult<TestPackage>(items, q.Page, q.PageSize, total);
    }

    /// <summary>
    /// Admin edits prices on service_packages. The public catalog and checkout read test_packages,
    /// so the listed price must win whenever a matching service package exists.
    /// </summary>
    private async Task ApplyListedPricesAsync(IList<TestPackage> packages, CancellationToken cancellationToken)
    {
        if (packages.Count == 0) return;

        var listed = await _servicePackagesCollection
            .Find(Builders<ServicePackage>.Filter.Eq(p => p.IsDeleted, false))
            .ToListAsync(cancellationToken);
        var byCode = new Dictionary<string, ServicePackage>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, ServicePackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in listed)
        {
            if (!string.IsNullOrWhiteSpace(item.Code)) byCode[item.Code.Trim()] = item;
            if (!string.IsNullOrWhiteSpace(item.Name)) byName[item.Name.Trim()] = item;
        }

        foreach (var package in packages)
        {
            ServicePackage? match = null;
            if (!string.IsNullOrWhiteSpace(package.PackageCode))
                byCode.TryGetValue(package.PackageCode.Trim(), out match);
            if (match == null && !string.IsNullOrWhiteSpace(package.PackageName))
                byName.TryGetValue(package.PackageName.Trim(), out match);
            if (match != null)
                package.Price = match.DiscountPrice ?? match.Price;
        }
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
            // Build alias maps between test_packages._id and package_code (e.g. PKG-PKG-GEN-01 <-> PKG-GEN-01, PKG-CBC-01 <-> CBC-01).
            var requestedIdToCodes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var codeToRequestedIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var lookupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddAlias(string reqId, string alias)
            {
                if (string.IsNullOrWhiteSpace(alias)) return;
                var a = alias.Trim();
                lookupKeys.Add(a);
                if (!requestedIdToCodes.TryGetValue(reqId, out var cList))
                {
                    cList = new List<string>();
                    requestedIdToCodes[reqId] = cList;
                }
                if (!cList.Contains(a, StringComparer.OrdinalIgnoreCase)) cList.Add(a);

                if (!codeToRequestedIds.TryGetValue(a, out var rList))
                {
                    rList = new List<string>();
                    codeToRequestedIds[a] = rList;
                }
                if (!rList.Contains(reqId, StringComparer.OrdinalIgnoreCase)) rList.Add(reqId);
            }

            foreach (var id in ids)
            {
                AddAlias(id, id);
                AddAlias(id, id.ToUpperInvariant());
            }

            var tpMatches = await _collection
                .Find(Builders<TestPackage>.Filter.Or(
                    Builders<TestPackage>.Filter.In(p => p.Id, ids),
                    Builders<TestPackage>.Filter.In(p => p.PackageCode, ids.Select(x => x.ToUpperInvariant()))))
                .ToListAsync(cancellationToken);

            foreach (var tp in tpMatches)
            {
                foreach (var reqId in ids)
                {
                    if (reqId.Equals(tp.Id, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(tp.PackageCode) && reqId.Equals(tp.PackageCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        AddAlias(reqId, tp.Id);
                        if (!string.IsNullOrWhiteSpace(tp.PackageCode))
                        {
                            AddAlias(reqId, tp.PackageCode);
                            AddAlias(reqId, tp.PackageCode.ToUpperInvariant());
                        }
                    }
                }
            }

            // Priority 1: Admin-managed service_packages.Tests (authoritative when non-empty).
            var allLookupList = lookupKeys.ToList();
            var spList = await _servicePackagesCollection
                .Find(Builders<ServicePackage>.Filter.And(
                    Builders<ServicePackage>.Filter.Eq(p => p.IsDeleted, false),
                    Builders<ServicePackage>.Filter.Or(
                        Builders<ServicePackage>.Filter.In(p => p.Id, allLookupList),
                        Builders<ServicePackage>.Filter.In(p => p.Code, allLookupList))))
                .ToListAsync(cancellationToken);

            var spWithTests = spList.Where(sp => sp.Tests != null && sp.Tests.Count > 0).ToList();
            if (spWithTests.Count > 0)
            {
                var allCritDocs = await _criteriaCollection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(cancellationToken);
                var critByCode = new Dictionary<string, BsonDocument>(StringComparer.OrdinalIgnoreCase);
                foreach (var cd in allCritDocs)
                {
                    var c = cd.Contains("criterionCode") && cd["criterionCode"].IsString ? cd["criterionCode"].AsString
                          : cd.Contains("code") && cd["code"].IsString ? cd["code"].AsString
                          : string.Empty;
                    if (string.IsNullOrWhiteSpace(c)) continue;
                    var isCanonical = cd.Contains("criterionCode") || cd.Contains("normalMin");
                    if (!critByCode.ContainsKey(c) || isCanonical) critByCode[c] = cd;
                }

                foreach (var sp in spWithTests)
                {
                    var list = new List<TestPackageCriterionSummaryDto>();
                    var order = 1;
                    foreach (var t in sp.Tests)
                    {
                        if (string.IsNullOrWhiteSpace(t.Code)) continue;
                        var unit = string.Empty;
                        string? refRange = null;
                        if (critByCode.TryGetValue(t.Code.Trim(), out var cd))
                        {
                            if (cd.TryGetValue("unit", out var uVal) && uVal.IsString) unit = uVal.AsString;
                            var min = cd.TryGetValue("normalMin", out var mn) && !mn.IsBsonNull
                                ? (mn.IsNumeric ? Math.Round(mn.ToDouble(), 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : mn.ToString())
                                : null;
                            var max = cd.TryGetValue("normalMax", out var mx) && !mx.IsBsonNull
                                ? (mx.IsNumeric ? Math.Round(mx.ToDouble(), 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : mx.ToString())
                                : null;
                            if (min != null && max != null) refRange = $"{min} – {max}";
                            else if (min != null) refRange = $">= {min}";
                            else if (max != null) refRange = $"<= {max}";
                        }
                        list.Add(new TestPackageCriterionSummaryDto
                        {
                            CriterionId = t.Code.Trim(),
                            Code = t.Code.Trim(),
                            Name = !string.IsNullOrWhiteSpace(t.Name) ? t.Name.Trim() : t.Code.Trim(),
                            Unit = unit,
                            ReferenceRange = refRange,
                            DisplayOrder = order++
                        });
                    }
                    if (list.Count > 0)
                    {
                        result[sp.Id] = list;
                        if (!string.IsNullOrWhiteSpace(sp.Code)) result[sp.Code] = list;
                        if (codeToRequestedIds.TryGetValue(sp.Id, out var rById))
                            foreach (var r in rById) result[r] = list;
                        if (!string.IsNullOrWhiteSpace(sp.Code) && codeToRequestedIds.TryGetValue(sp.Code, out var rByCode))
                            foreach (var r in rByCode) result[r] = list;
                    }
                }
            }

            // Priority 2: Fallback to legacy test_package_criteria mappings for packages whose service_packages.Tests is empty.
            var remainingIds = ids.Where(id => !result.ContainsKey(id) || result[id].Count == 0).ToList();
            if (remainingIds.Count > 0)
            {
                var mapLookupKeys = remainingIds
                    .SelectMany(id => requestedIdToCodes.TryGetValue(id, out var aliases) ? aliases : new List<string> { id })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var maps = await _mapCollection
                    .Find(Builders<BsonDocument>.Filter.In("package_id", mapLookupKeys))
                    .ToListAsync(cancellationToken);

                if (maps.Count > 0)
                {
                    var criterionIds = maps
                        .Select(m => m.GetValue("criteria_id", BsonNull.Value).ToString() ?? string.Empty)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct()
                        .ToList();

                    var bsonIds = new List<BsonValue>();
                    foreach (var id in criterionIds)
                    {
                        bsonIds.Add(new BsonString(id));
                        if (ObjectId.TryParse(id, out var oid))
                        {
                            bsonIds.Add(new BsonObjectId(oid));
                        }
                    }

                    var criteriaDocs = await _criteriaCollection
                        .Find(Builders<BsonDocument>.Filter.In("_id", bsonIds))
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
                                           crit.Contains("criterionCode") ? crit["criterionCode"].AsString :
                                           crit.Contains("criterion_code") ? crit["criterion_code"].AsString : string.Empty;
                                var name = crit.Contains("name") ? crit["name"].AsString :
                                           crit.Contains("criterionName") ? crit["criterionName"].AsString :
                                           crit.Contains("criterion_name") ? crit["criterion_name"].AsString : string.Empty;
                                var unit = crit.Contains("unit") && !crit["unit"].IsBsonNull ? crit["unit"].AsString : string.Empty;

                                string? refRange = null;
                                if (crit.Contains("reference_range") && !crit["reference_range"].IsBsonNull)
                                {
                                    refRange = crit["reference_range"].AsString;
                                }
                                else if (crit.Contains("referenceRange") && !crit["referenceRange"].IsBsonNull)
                                {
                                    refRange = crit["referenceRange"].AsString;
                                }
                                else if (crit.Contains("normalMin") || crit.Contains("normalMax"))
                                {
                                    var min = crit.Contains("normalMin") && !crit["normalMin"].IsBsonNull ? crit["normalMin"].ToString() : null;
                                    var max = crit.Contains("normalMax") && !crit["normalMax"].IsBsonNull ? crit["normalMax"].ToString() : null;
                                    if (min != null && max != null) refRange = $"{min} – {max}";
                                    else if (min != null) refRange = $">= {min}";
                                    else if (max != null) refRange = $"<= {max}";
                                }

                                list.Add(new TestPackageCriterionSummaryDto
                                {
                                    CriterionId = critId,
                                    Code = code,
                                    Name = name,
                                    Unit = unit,
                                    ReferenceRange = refRange,
                                    DisplayOrder = displayOrder
                                });
                            }
                        }
                        var ordered = list.OrderBy(x => x.DisplayOrder).ToList();
                        result[group.Key] = ordered;
                        if (codeToRequestedIds.TryGetValue(group.Key, out var reqs))
                        {
                            foreach (var r in reqs)
                            {
                                if (!result.ContainsKey(r) || result[r].Count == 0)
                                    result[r] = ordered;
                            }
                        }
                    }
                }
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
