using BMS.Core.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Serializers;

namespace BMS.Infrastructure.Mongo;

/// <summary>Tên collection (cấu hình được trong appsettings: MongoDB:*CollectionName).</summary>
public sealed record MongoCollections(
    string Users,
    string Roles,
    string RefreshTokens,
    string ServicePackages = "service_packages",
    string PasswordResetTokens = "password_reset_tokens");

public static class MongoMappings
{
    private static bool _registered;
    private static readonly object Lock = new();

    private static readonly NullableSerializer<DateTime> UtcNullable =
        new(new DateTimeSerializer(DateTimeKind.Utc));

    /// <summary>Gọi 1 lần khi khởi động, TRƯỚC khi dùng collection.</summary>
    public static void Register()
    {
        lock (Lock)
        {
            if (_registered) return;

            BsonClassMap.RegisterClassMap<User>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true); // document có thể có field thừa
                cm.MapIdMember(x => x.Id)
                  .SetSerializer(new StringSerializer(BsonType.ObjectId))
                  .SetIdGenerator(StringObjectIdGenerator.Instance);
            });

            BsonClassMap.RegisterClassMap<Role>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdMember(x => x.Id); // _id: int
                cm.MapMember(x => x.RoleName).SetElementName("role_name");
                cm.MapMember(x => x.Description).SetElementName("description");
                cm.MapMember(x => x.Permissions).SetElementName("permissions");
            });

            BsonClassMap.RegisterClassMap<RefreshToken>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdMember(x => x.Id)
                  .SetSerializer(new StringSerializer(BsonType.ObjectId))
                  .SetIdGenerator(StringObjectIdGenerator.Instance);
                cm.MapMember(x => x.UserId).SetSerializer(new StringSerializer(BsonType.ObjectId));
            });

            BsonClassMap.RegisterClassMap<PasswordResetToken>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdMember(x => x.Id)
                  .SetSerializer(new StringSerializer(BsonType.ObjectId))
                  .SetIdGenerator(StringObjectIdGenerator.Instance);
                cm.MapMember(x => x.UserId).SetSerializer(new StringSerializer(BsonType.ObjectId));
                cm.MapMember(x => x.CreatedAt).SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
                cm.MapMember(x => x.ExpiresAt).SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
                cm.MapMember(x => x.UsedAt).SetSerializer(UtcNullable);
            });

            BsonClassMap.RegisterClassMap<ServicePackageTest>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
            });

            BsonClassMap.RegisterClassMap<ServicePackage>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdMember(x => x.Id)
                  .SetSerializer(new StringSerializer(BsonType.ObjectId))
                  .SetIdGenerator(StringObjectIdGenerator.Instance);
                // Tiền lưu Decimal128 để so sánh/sắp xếp theo giá đúng và không mất độ chính xác
                cm.MapMember(x => x.Price).SetSerializer(new DecimalSerializer(BsonType.Decimal128));
                cm.MapMember(x => x.DiscountPrice)
                  .SetSerializer(new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));
                cm.MapMember(x => x.CreatedAt).SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
                cm.MapMember(x => x.UpdatedAt).SetSerializer(UtcNullable);
                cm.MapMember(x => x.DeletedAt).SetSerializer(UtcNullable);
            });

            _registered = true;
        }
    }
}
