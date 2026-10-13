using System.Text.RegularExpressions;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Mongo;
using BMS.Infrastructure.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Api.Startup;

/// <summary>Tạo index cần thiết cho login + (chỉ Development) sửa hash mẫu không hợp lệ.</summary>
public sealed partial class MongoInitializer(
    IMongoDatabase db,
    MongoCollections names,
    IPasswordHasher hasher,
    IConfiguration config,
    IHostEnvironment env,
    ILogger<MongoInitializer> logger) : IHostedService
{
    [GeneratedRegex(@"^\$2[abxy]\$\d{2}\$[./A-Za-z0-9]{53}$")]
    private static partial Regex BcryptFormat();

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await CreateIndexesAsync(ct);
            if (env.IsDevelopment()) await FixSampleHashesAsync(ct);
        }
        catch (Exception ex)
        {
            // Không chặn app khởi động nếu Mongo tạm thời chưa sẵn sàng
            logger.LogError(ex, "MongoInitializer failed");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task CreateIndexesAsync(CancellationToken ct)
    {
        var users = db.GetCollection<User>(names.Users);
        var ci = UserRepository.CaseInsensitive;
        await users.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.Username),
                new CreateIndexOptions { Name = "ix_username_ci", Collation = ci }),
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Name = "ix_email_ci", Collation = ci })
        ], ct);

        var tokens = db.GetCollection<RefreshToken>(names.RefreshTokens);
        await tokens.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<RefreshToken>(Builders<RefreshToken>.IndexKeys.Ascending(t => t.TokenHash),
                new CreateIndexOptions { Name = "ux_token_hash", Unique = true }),
            new CreateIndexModel<RefreshToken>(Builders<RefreshToken>.IndexKeys.Ascending(t => t.FamilyId),
                new CreateIndexOptions { Name = "ix_family" }),
            new CreateIndexModel<RefreshToken>(Builders<RefreshToken>.IndexKeys.Ascending(t => t.UserId),
                new CreateIndexOptions { Name = "ix_user" }),
            // TTL: tự xoá token hết hạn sau 7 ngày (giữ lại để phát hiện reuse)
            new CreateIndexModel<RefreshToken>(Builders<RefreshToken>.IndexKeys.Ascending(t => t.ExpiresAt),
                new CreateIndexOptions { Name = "ttl_expires", ExpireAfter = TimeSpan.FromDays(7) })
        ], ct);

        // Token đặt lại mật khẩu: tra theo hash, TTL tự xoá sau khi hết hạn 1 ngày
        var resets = db.GetCollection<PasswordResetToken>(names.PasswordResetTokens);
        await resets.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<PasswordResetToken>(Builders<PasswordResetToken>.IndexKeys.Ascending(t => t.TokenHash),
                new CreateIndexOptions { Name = "ux_reset_token_hash", Unique = true }),
            new CreateIndexModel<PasswordResetToken>(Builders<PasswordResetToken>.IndexKeys.Ascending(t => t.UserId),
                new CreateIndexOptions { Name = "ix_reset_user" }),
            new CreateIndexModel<PasswordResetToken>(Builders<PasswordResetToken>.IndexKeys.Ascending(t => t.ExpiresAt),
                new CreateIndexOptions { Name = "ttl_reset_expires", ExpireAfter = TimeSpan.FromDays(1) })
        ], ct);

        // Gói dịch vụ (collection tự được tạo khi tạo index nếu chưa có)
        var packages = db.GetCollection<ServicePackage>(names.ServicePackages);
        await packages.Indexes.CreateManyAsync(
        [
            // Mã gói duy nhất trong các gói CHƯA xoá -> xoá mềm xong vẫn dùng lại được mã
            new CreateIndexModel<ServicePackage>(Builders<ServicePackage>.IndexKeys.Ascending(p => p.Code),
                new CreateIndexOptions<ServicePackage>
                {
                    Name = "ux_package_code_active",
                    Unique = true,
                    PartialFilterExpression = new BsonDocument("IsDeleted", false)
                }),
            new CreateIndexModel<ServicePackage>(
                Builders<ServicePackage>.IndexKeys.Ascending(p => p.IsDeleted).Ascending(p => p.IsActive).Ascending(p => p.Name),
                new CreateIndexOptions { Name = "ix_package_list" }),
            new CreateIndexModel<ServicePackage>(Builders<ServicePackage>.IndexKeys.Ascending(p => p.Price),
                new CreateIndexOptions { Name = "ix_package_price" })
        ], ct);
    }

    /// <summary>
    /// Dữ liệu seed có hash giả ("$2a$11$eD8.SampleHashAdmin01") nên không đăng nhập được.
    /// Ở môi trường Development, nếu cấu hình DevSeed:DefaultPassword thì hash lại các tài khoản đó.
    /// </summary>
    private async Task FixSampleHashesAsync(CancellationToken ct)
    {
        var password = config["DevSeed:DefaultPassword"];
        if (string.IsNullOrWhiteSpace(password)) return;

        var users = db.GetCollection<User>(names.Users);
        var all = await users.Find(FilterDefinition<User>.Empty).ToListAsync(ct);
        var fixedCount = 0;
        string? newHash = null;

        foreach (var u in all.Where(u => !BcryptFormat().IsMatch(u.Password ?? "")))
        {
            newHash ??= hasher.Hash(password);
            await users.UpdateOneAsync(x => x.Id == u.Id, Builders<User>.Update.Set(x => x.Password, newHash), cancellationToken: ct);
            fixedCount++;
        }

        if (fixedCount > 0)
            logger.LogWarning("[DEV ONLY] Re-hashed {Count} sample account(s) with DevSeed:DefaultPassword", fixedCount);
    }
}
