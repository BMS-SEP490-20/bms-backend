using BMS.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Data;

/// <summary>
/// Quản lý kết nối MongoDB và các Collection chuyên biệt trong hệ thống BMS.
/// Khởi tạo TTL Index cho các token tạm thời nhằm tối ưu hóa bộ nhớ cơ sở dữ liệu.
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
            _logger.LogInformation("Đã kết nối MongoDB thành công: {DatabaseName}", databaseName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể kết nối trực tiếp tới MongoDB. Hệ thống sẽ kích hoạt In-Memory Repository Fallback cho môi trường Dev.");
        }
    }

    /// <summary>
    /// Collection lưu trữ mã xác thực OTP ("otp_tokens").
    /// </summary>
    public IMongoCollection<OtpTokenEntity>? OtpTokens =>
        _database?.GetCollection<OtpTokenEntity>("otp_tokens");

    /// <summary>
    /// Thiết lập các chỉ mục cần thiết (Index), bao gồm TTL Index trên trường ExpiresAt.
    /// </summary>
    private void EnsureIndexes()
    {
        if (OtpTokens == null) return;

        try
        {
            // 1. TTL Index: MongoDB tự động dọn dẹp các bản ghi khi thời gian chạm mốc ExpiresAt
            var ttlIndexKeys = Builders<OtpTokenEntity>.IndexKeys.Ascending(x => x.ExpiresAt);
            var ttlIndexOptions = new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "TTL_ExpiresAt" };
            OtpTokens.Indexes.CreateOne(new CreateIndexModel<OtpTokenEntity>(ttlIndexKeys, ttlIndexOptions));

            // 2. Compound Index trên Recipient và Purpose để tối ưu tốc độ truy vấn
            var recipientIndexKeys = Builders<OtpTokenEntity>.IndexKeys
                .Ascending(x => x.Recipient)
                .Ascending(x => x.Purpose);
            OtpTokens.Indexes.CreateOne(new CreateIndexModel<OtpTokenEntity>(recipientIndexKeys, new CreateIndexOptions { Name = "IX_Recipient_Purpose" }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Không thể tạo tự động Indexes cho MongoDB: {Message}", ex.Message);
        }
    }
}
