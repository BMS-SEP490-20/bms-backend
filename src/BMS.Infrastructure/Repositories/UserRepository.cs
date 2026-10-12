using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    /// <summary>So khớp không phân biệt hoa/thường (Secondary = bỏ qua case, giữ dấu).</summary>
    public static readonly Collation CaseInsensitive = new("en", strength: CollationStrength.Secondary);

    private readonly IMongoCollection<User> _users;

    public UserRepository(IMongoDatabase db, MongoCollections names)
        => _users = db.GetCollection<User>(names.Users);

    public async Task<User?> FindByLoginAsync(string usernameOrEmail, CancellationToken ct = default)
    {
        var filter = Builders<User>.Filter.Or(
            Builders<User>.Filter.Eq(u => u.Username, usernameOrEmail),
            Builders<User>.Filter.Eq(u => u.Email, usernameOrEmail));

        return await _users.Find(filter, new FindOptions { Collation = CaseInsensitive })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<User?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        return await _users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);
    }

    public async Task<int> IncrementFailedLoginAsync(string id, CancellationToken ct = default)
    {
        var updated = await _users.FindOneAndUpdateAsync(
            Builders<User>.Filter.Eq(u => u.Id, id),
            Builders<User>.Update.Inc(u => u.FailedLoginCount, 1),
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
            ct);
        return updated?.FailedLoginCount ?? 0;
    }

    public Task LockAsync(string id, DateTime lockoutEnd, CancellationToken ct = default)
        => _users.UpdateOneAsync(
            Builders<User>.Filter.Eq(u => u.Id, id),
            Builders<User>.Update
                .Set(u => u.LockoutEnd, lockoutEnd)
                .Set(u => u.FailedLoginCount, 0),
            cancellationToken: ct);

    public Task RecordSuccessfulLoginAsync(string id, DateTime now, string? newPasswordHash, CancellationToken ct = default)
    {
        var update = Builders<User>.Update
            .Set(u => u.LastLoginAt, now)
            .Set(u => u.FailedLoginCount, 0)
            .Unset(u => u.LockoutEnd);

        if (newPasswordHash is not null)
            update = update.Set(u => u.Password, newPasswordHash);

        return _users.UpdateOneAsync(Builders<User>.Filter.Eq(u => u.Id, id), update, cancellationToken: ct);
    }
}
