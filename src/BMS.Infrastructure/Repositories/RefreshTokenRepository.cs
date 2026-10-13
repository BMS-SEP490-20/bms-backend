using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Mongo;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IMongoCollection<RefreshToken> _tokens;

    public RefreshTokenRepository(IMongoDatabase db, MongoCollections names)
        => _tokens = db.GetCollection<RefreshToken>(names.RefreshTokens);

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
        => _tokens.InsertOneAsync(token, cancellationToken: ct);

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => await _tokens.Find(t => t.TokenHash == tokenHash).FirstOrDefaultAsync(ct);

    public async Task<bool> TryRevokeAsync(string id, DateTime now, string reason, string? replacedByHash, CancellationToken ct = default)
    {
        var result = await _tokens.UpdateOneAsync(
            Builders<RefreshToken>.Filter.And(
                Builders<RefreshToken>.Filter.Eq(t => t.Id, id),
                Builders<RefreshToken>.Filter.Eq(t => t.RevokedAt, null)),
            Builders<RefreshToken>.Update
                .Set(t => t.RevokedAt, now)
                .Set(t => t.RevokedReason, reason)
                .Set(t => t.ReplacedByTokenHash, replacedByHash),
            cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    public Task RevokeFamilyAsync(string familyId, DateTime now, string reason, CancellationToken ct = default)
        => _tokens.UpdateManyAsync(
            Builders<RefreshToken>.Filter.And(
                Builders<RefreshToken>.Filter.Eq(t => t.FamilyId, familyId),
                Builders<RefreshToken>.Filter.Eq(t => t.RevokedAt, null)),
            Builders<RefreshToken>.Update.Set(t => t.RevokedAt, now).Set(t => t.RevokedReason, reason),
            cancellationToken: ct);

    public Task RevokeAllForUserAsync(string userId, DateTime now, string reason, CancellationToken ct = default)
        => _tokens.UpdateManyAsync(
            Builders<RefreshToken>.Filter.And(
                Builders<RefreshToken>.Filter.Eq(t => t.UserId, userId),
                Builders<RefreshToken>.Filter.Eq(t => t.RevokedAt, null)),
            Builders<RefreshToken>.Update.Set(t => t.RevokedAt, now).Set(t => t.RevokedReason, reason),
            cancellationToken: ct);
}
