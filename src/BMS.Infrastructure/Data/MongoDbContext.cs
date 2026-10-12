using BMS.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Data;

/// <summary>
/// Manages MongoDB database connections, collections, and index configurations.
/// Enforces TTL Index creation on ephemeral token collections to automate memory cleanup.
/// </summary>
public class MongoDbContext
{
    private readonly IMongoDatabase? _database;
    private readonly ILogger<MongoDbContext> _logger;

    public MongoDbContext(IConfiguration configuration, ILogger<MongoDbContext> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("MongoDb")
            ?? configuration["MongoDbSettings:ConnectionString"]
            ?? "mongodb://localhost:27017";

        var databaseName = configuration["MongoDbSettings:DatabaseName"] ?? "BmsDatabase";

        try
        {
            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
            EnsureIndexes();
            _logger.LogInformation("Connected successfully to MongoDB database: {DatabaseName}", databaseName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not establish direct MongoDB connection. In-Memory fallback will handle local development.");
        }
    }

    /// <summary>
    /// Collection persisting OTP verification tokens ("otp_tokens").
    /// </summary>
    public IMongoCollection<OtpTokenEntity>? OtpTokens =>
        _database?.GetCollection<OtpTokenEntity>("otp_tokens");

    /// <summary>
    /// Initializes necessary database indexes, notably the TTL Index on ExpiresAt.
    /// </summary>
    private void EnsureIndexes()
    {
        if (OtpTokens == null) return;

        try
        {
            // 1. TTL Index: MongoDB automatically reclaims documents once UTC time reaches ExpiresAt
            var ttlIndexKeys = Builders<OtpTokenEntity>.IndexKeys.Ascending(x => x.ExpiresAt);
            var ttlIndexOptions = new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "TTL_ExpiresAt" };
            OtpTokens.Indexes.CreateOne(new CreateIndexModel<OtpTokenEntity>(ttlIndexKeys, ttlIndexOptions));

            // 2. Compound Index on Recipient and Purpose for high-throughput lookup performance
            var recipientIndexKeys = Builders<OtpTokenEntity>.IndexKeys
                .Ascending(x => x.Recipient)
                .Ascending(x => x.Purpose);
            OtpTokens.Indexes.CreateOne(new CreateIndexModel<OtpTokenEntity>(recipientIndexKeys, new CreateIndexOptions { Name = "IX_Recipient_Purpose" }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unable to automatically register MongoDB indexes: {Message}", ex.Message);
        }
    }
}
