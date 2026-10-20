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
    string PasswordResetTokens = "password_reset_tokens",
    string TestCriteria = "test_criteria");

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

            var flexibleIdSerializer = new FlexibleStringObjectIdSerializer();

            BsonClassMap.RegisterClassMap<User>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true); // document có thể có field thừa
                cm.MapIdMember(x => x.Id)
                  .SetSerializer(flexibleIdSerializer)
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
                  .SetSerializer(flexibleIdSerializer)
                  .SetIdGenerator(StringObjectIdGenerator.Instance);
                cm.MapMember(x => x.UserId).SetSerializer(flexibleIdSerializer);
            });

            BsonClassMap.RegisterClassMap<PasswordResetToken>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdMember(x => x.Id)
                  .SetSerializer(flexibleIdSerializer)
                  .SetIdGenerator(StringObjectIdGenerator.Instance);
                cm.MapMember(x => x.UserId).SetSerializer(flexibleIdSerializer);
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
                  .SetSerializer(flexibleIdSerializer)
                  .SetIdGenerator(StringObjectIdGenerator.Instance);
                // Tiền lưu Decimal128 để so sánh/sắp xếp theo giá đúng và không mất độ chính xác
                cm.MapMember(x => x.Price).SetSerializer(new DecimalSerializer(BsonType.Decimal128));
                cm.MapMember(x => x.DiscountPrice)
                  .SetSerializer(new NullableSerializer<decimal>(new DecimalSerializer(BsonType.Decimal128)));
                cm.MapMember(x => x.CreatedAt).SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
                cm.MapMember(x => x.UpdatedAt).SetSerializer(UtcNullable);
                cm.MapMember(x => x.DeletedAt).SetSerializer(UtcNullable);
            });
            BsonClassMap.RegisterClassMap<TestCriterion>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
                cm.MapIdMember(x => x.Id); // _id: string, vd "CRT-RBC"
                cm.MapMember(x => x.Code).SetElementName("code");
                cm.MapMember(x => x.Name).SetElementName("name");
                cm.MapMember(x => x.Unit).SetElementName("unit");
                cm.MapMember(x => x.Category).SetElementName("category");
            });
            _registered = true;
        }
    }
}

/// <summary>
/// Serializer cho phép đọc/ghi cả ObjectId (24-hex) lẫn String tuỳ chỉnh mà không lỗi FormatException.
/// </summary>
public sealed class FlexibleStringObjectIdSerializer : SerializerBase<string>
{
    public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var type = context.Reader.GetCurrentBsonType();
        return type switch
        {
            BsonType.ObjectId => context.Reader.ReadObjectId().ToString(),
            BsonType.String => context.Reader.ReadString(),
            BsonType.Int32 => context.Reader.ReadInt32().ToString(),
            BsonType.Int64 => context.Reader.ReadInt64().ToString(),
            BsonType.Null => ReadNull(context),
            _ => context.Reader.ReadString()
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
    {
        if (value is null)
        {
            context.Writer.WriteNull();
            return;
        }

        if (ObjectId.TryParse(value, out var oid))
        {
            context.Writer.WriteObjectId(oid);
        }
        else
        {
            context.Writer.WriteString(value);
        }
    }

    private static string ReadNull(BsonDeserializationContext context)
    {
        context.Reader.ReadNull();
        return string.Empty;
    }
}
