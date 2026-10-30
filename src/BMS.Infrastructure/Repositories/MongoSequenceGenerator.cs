using BMS.Application.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <inheritdoc />
public sealed class MongoSequenceGenerator(IMongoDatabase database) : ISequenceGenerator
{
    /// <summary>Collection holding one document per counter: <c>{ _id: key, seq: n }</c>.</summary>
    public const string CollectionName = "counters";

    private readonly IMongoCollection<BsonDocument> _counters = database.GetCollection<BsonDocument>(CollectionName);

    /// <inheritdoc />
    public async Task<long> NextAsync(string key, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", key);
        var update = Builders<BsonDocument>.Update.Inc("seq", 1L);
        var options = new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After };
        // WHY the retry: two concurrent upserts of a not-yet-existing _id can race and one gets E11000; the
        // document exists after that, so the retried $inc is a plain atomic increment.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var doc = await _counters.FindOneAndUpdateAsync(filter, update, options, ct);
                return doc["seq"].ToInt64();
            }
            catch (MongoCommandException ex) when (ex.Code == 11000 && attempt < 3) { }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey && attempt < 3) { }
        }
    }
}
