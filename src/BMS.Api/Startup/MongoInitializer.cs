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
    IUserRoleMigration roleMigration,
    IFlowIndexInitializer flowIndexes,
    ILogger<MongoInitializer> logger) : IHostedService
{
    [GeneratedRegex(@"^\$2[abxy]\$\d{2}\$[./A-Za-z0-9]{53}$")]
    private static partial Regex BcryptFormat();

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await CreateIndexesAsync(ct);
            // WHY: one-time, idempotent data fix for the 6-role model (DOT2 follow-up 5); runs in every
            // environment because production data also holds RoleId 9 accounts.
            await roleMigration.MigrateLegacyPatientRoleIdAsync(ct);
            await flowIndexes.EnsureAsync(ct);
            if (env.IsDevelopment())
            {
                await FixSampleHashesAsync(ct);
                await SeedServicePackagesIfEmptyAsync(ct);
            }
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
        var rawCol = db.GetCollection<BsonDocument>(names.Users);
        var all = await users.Find(FilterDefinition<User>.Empty).ToListAsync(ct);
        var fixedCount = 0;
        string? newHash = null;

        foreach (var u in all.Where(u => !BcryptFormat().IsMatch(u.PasswordHash ?? "")))
        {
            newHash ??= hasher.Hash(password);
            var idFilter = ObjectId.TryParse(u.Id, out var oid)
                ? Builders<BsonDocument>.Filter.Eq("_id", oid)
                : Builders<BsonDocument>.Filter.Eq("_id", u.Id);

            var update = Builders<BsonDocument>.Update
                .Set("Password", newHash)
                .Set("PasswordHash", newHash)
                .Set("passwordHash", newHash);

            await rawCol.UpdateOneAsync(idFilter, update, cancellationToken: ct);
            fixedCount++;
        }

        if (fixedCount > 0)
            logger.LogWarning("[DEV ONLY] Re-hashed {Count} sample account(s) with DevSeed:DefaultPassword", fixedCount);
    }

    private async Task SeedServicePackagesIfEmptyAsync(CancellationToken ct)
    {
        var packages = db.GetCollection<ServicePackage>(names.ServicePackages);
        var count = await packages.CountDocumentsAsync(FilterDefinition<ServicePackage>.Empty, cancellationToken: ct);
        if (count > 0) return;

        var now = DateTime.UtcNow;
        var initialPackages = new List<ServicePackage>
        {
            new()
            {
                Code = "PKG_GEN_01",
                Name = "Gói Xét Nghiệm Tổng Quát Cơ Bản",
                Description = "Kiểm tra công thức máu, đường huyết, mỡ máu, chức năng gan thận cơ bản.",
                Category = "General Checkup",
                PreparationNotes = "Nhịn ăn sáng từ 8-12 tiếng trước khi lấy máu.",
                Price = 650000m,
                DiscountPrice = 585000m,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = now,
                CreatedBy = "SYSTEM",
                Tests =
                [
                    new ServicePackageTest { Code = "RBC", Name = "Số lượng Hồng cầu (Red Blood Cells)" },
                    new ServicePackageTest { Code = "WBC", Name = "Số lượng Bạch cầu (White Blood Cells)" },
                    new ServicePackageTest { Code = "HGB", Name = "Huyết sắc tố (Hemoglobin)" },
                    new ServicePackageTest { Code = "PLT", Name = "Số lượng Tiểu cầu (Platelets)" },
                    new ServicePackageTest { Code = "GLU", Name = "Đường huyết lúc đói (Fasting Glucose)" },
                    new ServicePackageTest { Code = "AST", Name = "Men gan AST (GOT)" },
                    new ServicePackageTest { Code = "ALT", Name = "Men gan ALT (GPT)" },
                    new ServicePackageTest { Code = "CRE", Name = "Creatinine máu (Chức năng thận)" },
                    new ServicePackageTest { Code = "CHOL", Name = "Cholesterol toàn phần" }
                ]
            },
            new()
            {
                Code = "PKG_LIVER_02",
                Name = "Gói Tầm Soát Chức Năng Gan Chuyên Sâu",
                Description = "Đánh giá tổn thương tế bào gan và chức năng bài tiết mật.",
                Category = "Specialized",
                PreparationNotes = "Không uống rượu bia ít nhất 24 giờ trước khi xét nghiệm.",
                Price = 320000m,
                DiscountPrice = 290000m,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = now,
                CreatedBy = "SYSTEM",
                Tests =
                [
                    new ServicePackageTest { Code = "AST", Name = "Men gan AST (GOT)" },
                    new ServicePackageTest { Code = "ALT", Name = "Men gan ALT (GPT)" }
                ]
            },
            new()
            {
                Code = "PKG_DIAB_03",
                Name = "Gói Theo Dõi Tiểu Đường & Mỡ Máu",
                Description = "Dành cho bệnh nhân có nguy cơ tim mạch, béo phì, đái tháo đường.",
                Category = "Chronic Disease",
                PreparationNotes = "Nhịn ăn tối thiểu 8 tiếng, chỉ uống nước lọc.",
                Price = 280000m,
                DiscountPrice = 250000m,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = now,
                CreatedBy = "SYSTEM",
                Tests =
                [
                    new ServicePackageTest { Code = "GLU", Name = "Đường huyết lúc đói (Fasting Glucose)" },
                    new ServicePackageTest { Code = "CHOL", Name = "Cholesterol toàn phần" },
                    new ServicePackageTest { Code = "CRE", Name = "Creatinine máu (Chức năng thận)" }
                ]
            }
        };

        await packages.InsertManyAsync(initialPackages, cancellationToken: ct);
        logger.LogInformation("Seeded {Count} default service packages into collection {Collection}", initialPackages.Count, names.ServicePackages);
    }
}
