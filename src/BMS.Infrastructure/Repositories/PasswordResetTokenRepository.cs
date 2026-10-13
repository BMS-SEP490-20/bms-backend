using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Mongo;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly IMongoCollection<PasswordResetToken> _col;

    public PasswordResetTokenRepository(IMongoDatabase db, MongoCollections names)
        => _col = db.GetCollection<PasswordResetToken>(names.PasswordResetTokens);

    public Task AddAsync(PasswordResetToken token, CancellationToken ct = default)
        => _col.InsertOneAsync(token, cancellationToken: ct);

    public async Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => await _col.Find(t => t.TokenHash == tokenHash).FirstOrDefaultAsync(ct);

    public async Task<bool> TryConsumeAsync(string id, DateTime now, CancellationToken ct = default)
    {
        var result = await _col.UpdateOneAsync(
            Builders<PasswordResetToken>.Filter.And(
                Builders<PasswordResetToken>.Filter.Eq(t => t.Id, id),
                Builders<PasswordResetToken>.Filter.Eq(t => t.UsedAt, null),
                Builders<PasswordResetToken>.Filter.Gt(t => t.ExpiresAt, now)),
            Builders<PasswordResetToken>.Update.Set(t => t.UsedAt, now),
            cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    public Task InvalidateAllForUserAsync(string userId, DateTime now, CancellationToken ct = default)
        => _col.UpdateManyAsync(
            Builders<PasswordResetToken>.Filter.And(
                Builders<PasswordResetToken>.Filter.Eq(t => t.UserId, userId),
                Builders<PasswordResetToken>.Filter.Eq(t => t.UsedAt, null)),
            Builders<PasswordResetToken>.Update.Set(t => t.UsedAt, now),
            cancellationToken: ct);

    public async Task<DateTime?> GetLastCreatedAtAsync(string userId, CancellationToken ct = default)
    {
        var last = await _col.Find(t => t.UserId == userId)
            .SortByDescending(t => t.CreatedAt)
            .Limit(1)
            .FirstOrDefaultAsync(ct);
        return last?.CreatedAt;
    }
}
