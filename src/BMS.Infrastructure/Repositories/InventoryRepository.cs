using System.Text.RegularExpressions;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>Persists existing reagent lots; embedded audit entries preserve stock change evidence.</summary>
public sealed class InventoryRepository : IInventoryRepository
{
    private readonly IMongoCollection<Reagent> _reagents;
    private readonly IMongoCollection<InventoryTransaction> _transactions;

    public InventoryRepository(IMongoDatabase database)
    {
        _reagents = database.GetCollection<Reagent>("reagents");
        _transactions = database.GetCollection<InventoryTransaction>("inventory_transactions");
        _reagents.Indexes.CreateMany([
            new(Builders<Reagent>.IndexKeys.Ascending(x => x.ExpiryDate).Ascending(x => x.Code).Ascending(x => x.Id)),
            new(Builders<Reagent>.IndexKeys.Ascending(x => x.Code).Ascending(x => x.LotNumber))
        ]);
        _transactions.Indexes.CreateOne(new CreateIndexModel<InventoryTransaction>(
            Builders<InventoryTransaction>.IndexKeys.Ascending(x => x.ReagentCode)
                .Descending(x => x.Timestamp).Descending(x => x.Id)));
    }

    public async Task<InventoryPage> ListAsync(InventorySearch search, DateTime today, CancellationToken ct = default)
    {
        var f = Builders<Reagent>.Filter;
        var filter = f.Empty;
        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            // Treat input as literal text, never as a caller-supplied regular expression.
            var regex = new BsonRegularExpression(Regex.Escape(search.Search), "i");
            filter &= f.Or(f.Regex(x => x.Code, regex), f.Regex(x => x.Name, regex),
                f.Regex(x => x.LotNumber, regex), f.Regex(x => x.Manufacturer, regex));
        }
        var day = DateTime.SpecifyKind(today.Date, DateTimeKind.Utc);
        var expired = f.Ne(x => x.ExpiryDate, null) & f.Lt(x => x.ExpiryDate, day);
        var soon = f.Gte(x => x.ExpiryDate, day) & f.Lt(x => x.ExpiryDate, day.AddDays(30));
        var low = new BsonDocumentFilterDefinition<Reagent>(new BsonDocument("$expr", new BsonDocument("$lte", new BsonArray {
            new BsonDocument("$ifNull", new BsonArray { "$quantity_in_stock", 0 }),
            new BsonDocument("$ifNull", new BsonArray { "$min_threshold", 0 })
        })));
        if (search.ExpiryFrom.HasValue) filter &= f.Gte(x => x.ExpiryDate, DateTime.SpecifyKind(search.ExpiryFrom.Value, DateTimeKind.Utc));
        if (search.ExpiryTo.HasValue) filter &= f.Lt(x => x.ExpiryDate, DateTime.SpecifyKind(search.ExpiryTo.Value.AddDays(1), DateTimeKind.Utc));
        filter &= search.Status switch
        {
            "LOW_STOCK" => low, "EXPIRED" => expired, "EXPIRING_SOON" => soon,
            "AVAILABLE" => f.Gte(x => x.ExpiryDate, day.AddDays(30)),
            "UNKNOWN_EXPIRY" => f.Eq(x => x.ExpiryDate, null), _ => f.Empty
        };
        var items = await _reagents.Find(filter).SortBy(x => x.ExpiryDate).ThenBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((search.Page - 1) * search.PageSize).Limit(search.PageSize).ToListAsync(ct);
        var total = await _reagents.CountDocumentsAsync(filter, cancellationToken: ct);
        var lowCount = await _reagents.CountDocumentsAsync(filter & low, cancellationToken: ct);
        var soonCount = await _reagents.CountDocumentsAsync(filter & soon, cancellationToken: ct);
        var expiredCount = await _reagents.CountDocumentsAsync(filter & expired, cancellationToken: ct);
        return new(items, total, lowCount, soonCount, expiredCount);
    }

    public async Task<bool> CreateAsync(Reagent item, CancellationToken ct = default)
    {
        var f = Builders<Reagent>.Filter;
        var duplicate = f.Regex(x => x.Code, new BsonRegularExpression("^" + Regex.Escape(item.Code) + "$", "i")) &
            f.Regex(x => x.LotNumber, new BsonRegularExpression("^" + Regex.Escape(item.LotNumber) + "$", "i"));
        if (await _reagents.Find(duplicate).AnyAsync(ct)) return false;
        try { await _reagents.InsertOneAsync(item, cancellationToken: ct); return true; }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }

    public Task<Reagent?> GetAsync(string id, CancellationToken ct = default) =>
        _reagents.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;

    public Task<Reagent?> UpdateAsync(Reagent item, long expectedRevision, InventoryChange change, CancellationToken ct = default)
    {
        var f = Builders<Reagent>.Filter;
        // Legacy documents have no revision; treat them as revision zero until their first mutation.
        var revision = expectedRevision == 0 ? f.Or(f.Eq(x => x.Revision, 0), f.Exists("revision", false)) : f.Eq(x => x.Revision, expectedRevision);
        var update = Builders<Reagent>.Update.Set(x => x.Name, item.Name).Set(x => x.Manufacturer, item.Manufacturer)
            .Set(x => x.Quantity, item.Quantity).Set(x => x.MinimumStock, item.MinimumStock).Set(x => x.ExpiryDate, item.ExpiryDate)
            .Inc(x => x.Revision, 1).Push(x => x.Changes, change);
        return _reagents.FindOneAndUpdateAsync(f.Eq(x => x.Id, item.Id) & revision, update,
            new FindOneAndUpdateOptions<Reagent> { ReturnDocument = ReturnDocument.After }, ct)!;
    }

    public async Task<(List<InventoryTransaction> Items, long Total)> GetTransactionsAsync(string reagentCode, CancellationToken ct = default)
    {
        var filter = Builders<InventoryTransaction>.Filter.Eq(x => x.ReagentCode, reagentCode);
        var total = await _transactions.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _transactions.Find(filter).SortByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.Id).Limit(20).ToListAsync(ct);
        return (items, total);
    }
}
