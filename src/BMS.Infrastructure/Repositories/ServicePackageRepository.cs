using System.Text.RegularExpressions;
using BMS.Application.DTOs.Common;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

public sealed class ServicePackageRepository : IServicePackageRepository
{
    private readonly IMongoDatabase _db;
    private readonly IMongoCollection<ServicePackage> _col;

    public ServicePackageRepository(IMongoDatabase db, MongoCollections names)
    {
        _db = db;
        _col = db.GetCollection<ServicePackage>(names.ServicePackages);
    }

    private static readonly FilterDefinitionBuilder<ServicePackage> F = Builders<ServicePackage>.Filter;

    public async Task<ServicePackage?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmed = id.Trim();

        if (ObjectId.TryParse(trimmed, out _))
        {
            var byId = await _col.Find(F.And(F.Eq(x => x.Id, trimmed), F.Eq(x => x.IsDeleted, false)))
                .FirstOrDefaultAsync(ct);
            if (byId != null) return byId;
        }

        // Cho phép tìm theo Code nếu id truyền vào là mã gói (vd: PKG_GEN_01)
        return await _col.Find(F.And(
                F.Or(F.Eq(x => x.Id, trimmed), F.Eq(x => x.Code, trimmed.ToUpperInvariant())),
                F.Eq(x => x.IsDeleted, false)))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PagedResult<ServicePackage>> SearchAsync(ServicePackageQuery q, CancellationToken ct = default)
    {
        var filters = new List<FilterDefinition<ServicePackage>> { F.Eq(x => x.IsDeleted, false) };

        if (q.IsActive is { } active) filters.Add(F.Eq(x => x.IsActive, active));
        if (q.MinPrice is { } min) filters.Add(F.Gte(x => x.Price, min));
        if (q.MaxPrice is { } max) filters.Add(F.Lte(x => x.Price, max));

        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            var catRx = new BsonRegularExpression($"^{Regex.Escape(q.Category.Trim())}$", "i");
            filters.Add(F.Regex(x => x.Category, catRx));
        }

        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            // Regex.Escape: coi keyword là văn bản thường, chặn regex injection / ReDoS
            var rx = new BsonRegularExpression(Regex.Escape(q.Keyword), "i");
            filters.Add(F.Or(
                F.Regex(x => x.Code, rx),
                F.Regex(x => x.Name, rx),
                F.Regex(x => x.Description, rx),
                F.Regex(x => x.Category, rx),
                F.Regex("Tests.Code", rx),
                F.Regex("Tests.Name", rx)));
        }

        var filter = F.And(filters);
        var total = await _col.CountDocumentsAsync(filter, cancellationToken: ct);

        var items = await _col.Find(filter)
            .Sort(BuildSort(q))
            .Skip((q.Page - 1) * q.PageSize)
            .Limit(q.PageSize)
            .ToListAsync(ct);

        return new PagedResult<ServicePackage>(items, q.Page, q.PageSize, total);
    }

    public async Task<IReadOnlyList<ServicePackage>> GetActiveByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default)
        => codes.Count == 0
            ? []
            : await _col.Find(F.In(x => x.Code, codes) & F.Eq(x => x.IsDeleted, false) & F.Eq(x => x.IsActive, true)).ToListAsync(ct);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken ct = default)
        => await _col.Find(F.And(F.Eq(x => x.Code, code), F.Eq(x => x.IsDeleted, false)))
            .Limit(1).AnyAsync(ct);

    public async Task<bool> HasActiveDependenciesAsync(string id, string code, CancellationToken ct = default)
    {
        try
        {
            var appointments = _db.GetCollection<BsonDocument>("appointments");
            var fb = Builders<BsonDocument>.Filter;
            var inactiveStatuses = new[] { "Cancelled", "Completed", "Rejected", "NoShow" };

            var packageMatch = fb.Or(
                fb.Eq("service_package_id", id),
                fb.Eq("service_package_code", code),
                fb.AnyEq("test_packages", id),
                fb.AnyEq("test_packages", code));

            var activeStatusFilter = fb.Nin("status", inactiveStatuses);
            return await appointments.Find(fb.And(packageMatch, activeStatusFilter))
                .Limit(1)
                .AnyAsync(ct);
        }
        catch
        {
            return false;
        }
    }

    public async Task InsertAsync(ServicePackage package, CancellationToken ct = default)
    {
        try
        {
            await _col.InsertOneAsync(package, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ServicePackageCodeAlreadyExistsException(package.Code);
        }
    }

    public async Task<bool> ReplaceAsync(ServicePackage package, CancellationToken ct = default)
    {
        var result = await _col.ReplaceOneAsync(
            F.And(F.Eq(x => x.Id, package.Id), F.Eq(x => x.IsDeleted, false)),
            package, cancellationToken: ct);
        return result.MatchedCount == 1;
    }

    public async Task<bool> SoftDeleteAsync(string id, DateTime now, string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        var result = await _col.UpdateOneAsync(
            F.And(F.Eq(x => x.Id, id), F.Eq(x => x.IsDeleted, false)),
            Builders<ServicePackage>.Update
                .Set(x => x.IsDeleted, true)
                .Set(x => x.IsActive, false)
                .Set(x => x.DeletedAt, now)
                .Set(x => x.DeletedBy, userId),
            cancellationToken: ct);
        return result.MatchedCount == 1;
    }

    private static SortDefinition<ServicePackage> BuildSort(ServicePackageQuery q)
    {
        var sb = Builders<ServicePackage>.Sort;
        SortDefinition<ServicePackage> primary = q.SortBy switch
        {
            "name"  => q.SortDesc ? sb.Descending(x => x.Name)  : sb.Ascending(x => x.Name),
            "code"  => q.SortDesc ? sb.Descending(x => x.Code)  : sb.Ascending(x => x.Code),
            "price" => q.SortDesc ? sb.Descending(x => x.Price) : sb.Ascending(x => x.Price),
            _       => q.SortDesc ? sb.Descending(x => x.CreatedAt) : sb.Ascending(x => x.CreatedAt)
        };
        // _id làm tiêu chí phụ để phân trang ổn định khi giá trị sort bị trùng
        return sb.Combine(primary, sb.Ascending(x => x.Id));
    }
}
