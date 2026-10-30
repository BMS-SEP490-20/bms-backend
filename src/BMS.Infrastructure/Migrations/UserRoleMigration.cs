using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Migrations;

/// <inheritdoc />
/// <remarks>
/// WHY: Quy trình ruling on DOT2 open question 5 — convert stored data once instead of mapping 9 → 6 forever.
/// A single conditional <c>UpdateMany</c> (filter <c>RoleId == 9</c>, <c>$set RoleId = 6</c>) is atomic per
/// document and naturally idempotent; the marker in <c>schema_migrations</c> records when and how many rows
/// were converted so the run is auditable. Only counts are logged, never user data (INV-4).
/// </remarks>
public sealed class UserRoleMigration(
    IMongoDatabase database,
    MongoCollections names,
    TimeProvider clock,
    ILogger<UserRoleMigration> logger) : IUserRoleMigration
{
    /// <summary>Stable id of this migration in <c>schema_migrations</c>.</summary>
    public const string MigrationId = "2026-10-30-user-roleid-9-to-6";

    private const string MigrationsCollection = "schema_migrations";

    /// <inheritdoc />
    public async Task<long> MigrateLegacyPatientRoleIdAsync(CancellationToken ct)
    {
        var users = database.GetCollection<User>(names.Users);
        var filter = Builders<User>.Filter.Eq(u => u.RoleId, User.LegacyPatientRoleId);
        var update = Builders<User>.Update.Set(u => u.RoleId, (int)UserRole.Patient);
        var result = await users.UpdateManyAsync(filter, update, cancellationToken: ct);

        var markers = database.GetCollection<BsonDocument>(MigrationsCollection);
        await markers.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", MigrationId),
            Builders<BsonDocument>.Update
                .SetOnInsert("firstAppliedAt", clock.GetUtcNow().UtcDateTime)
                .Set("lastRunAt", clock.GetUtcNow().UtcDateTime)
                .Inc("convertedTotal", result.ModifiedCount),
            new UpdateOptions { IsUpsert = true },
            ct);

        logger.LogInformation(
            "Migration {MigrationId}: converted {Count} account(s) from RoleId {Legacy} to {Patient}",
            MigrationId, result.ModifiedCount, User.LegacyPatientRoleId, (int)UserRole.Patient);
        return result.ModifiedCount;
    }
}
