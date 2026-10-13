
using System.Collections.Concurrent;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private static readonly Collation CaseInsensitive =
        new("en", strength: CollationStrength.Secondary);

    private readonly MongoDbContext _context;
    private readonly ILogger<UserRepository> _logger;

    private static readonly ConcurrentDictionary<string, User>
        _memoryUsers = new();

    public UserRepository(
        MongoDbContext context,
        ILogger<UserRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    // =========================================================
    // QUERY
    // =========================================================

    public async Task<User?> GetByIdAsync(
        string id,
        CancellationToken ct = default)
    {
        if (_context.Users != null)
        {
            try
            {
                var filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.Eq(u => u.Id, id),
                    Builders<User>.Filter.Eq(u => u.IsDeleted, false));

                return await _context.Users
                    .Find(filter)
                    .FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to query user by ID. Falling back to memory.");
            }
        }

        return _memoryUsers.TryGetValue(id, out var user)
            && !user.IsDeleted
                ? user
                : null;
    }

    public async Task<User?> FindByIdAsync(
        string id,
        CancellationToken ct = default)
    {
        return await GetByIdAsync(id, ct);
    }

    public async Task<User?> GetByEmailOrUsernameAsync(
        string identifier,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var keyword = identifier.Trim();

        if (_context.Users != null)
        {
            try
            {
                var filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.Or(
                        Builders<User>.Filter.Eq(
                            u => u.Email, keyword),
                        Builders<User>.Filter.Eq(
                            u => u.Username, keyword)),
                    Builders<User>.Filter.Eq(
                        u => u.IsDeleted, false));

                return await _context.Users
                    .Find(filter)
                    .Collation(CaseInsensitive)
                    .FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to query user by identifier. Falling back to memory.");
            }
        }

        return _memoryUsers.Values.FirstOrDefault(u =>
            !u.IsDeleted &&
            (string.Equals(
                 u.Email, keyword,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 u.Username, keyword,
                 StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<User?> FindByLoginAsync(
        string usernameOrEmail,
        CancellationToken ct = default)
    {
        return await GetByEmailOrUsernameAsync(
            usernameOrEmail, ct);
    }

    public async Task<User?> FindByEmailAsync(
        string email,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var keyword = email.Trim();

        if (_context.Users != null)
        {
            try
            {
                var filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.Eq(
                        u => u.Email, keyword),
                    Builders<User>.Filter.Eq(
                        u => u.IsDeleted, false));

                return await _context.Users
                    .Find(filter)
                    .Collation(CaseInsensitive)
                    .FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to query user by email. Falling back to memory.");
            }
        }

        return _memoryUsers.Values.FirstOrDefault(u =>
            !u.IsDeleted &&
            string.Equals(
                u.Email, keyword,
                StringComparison.OrdinalIgnoreCase));
    }

    // =========================================================
    // COUNT BY ROLE
    // =========================================================

    public async Task<long> CountUsersByRoleIdAsync(
        string roleId,
        CancellationToken ct = default)
    {
        if (_context.Users != null)
        {
            try
            {
                var roleFilters = new List<FilterDefinition<User>>
                {
                    Builders<User>.Filter.Eq(
                        u => u.RoleId, roleId)
                };

                if (Enum.TryParse<UserRole>(
                    roleId, true, out var parsedRole))
                {
                    roleFilters.Add(
                        Builders<User>.Filter.Eq(
                            u => u.Role, parsedRole));
                }

                var filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.Or(roleFilters),
                    Builders<User>.Filter.Eq(
                        u => u.IsDeleted, false));

                return await _context.Users.CountDocumentsAsync(
                    filter,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to count users by role. Falling back to memory.");
            }
        }

        return _memoryUsers.Values.LongCount(u =>
            !u.IsDeleted &&
            (u.RoleId == roleId ||
             string.Equals(
                 u.Role.ToString(),
                 roleId,
                 StringComparison.OrdinalIgnoreCase)));
    }

    // =========================================================
    // CREATE / UPDATE
    // =========================================================

    public async Task CreateAsync(
        User user,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id))
        {
            user.Id = ObjectId.GenerateNewId().ToString();
        }

        if (_context.Users != null)
        {
            try
            {
                await _context.Users.InsertOneAsync(
                    user,
                    cancellationToken: ct);

                _memoryUsers[user.Id] = user;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to insert user into MongoDB.");
                throw;
            }
        }

        _memoryUsers[user.Id] = user;
    }

    public async Task UpdateAsync(
        User user,
        CancellationToken ct = default)
    {
        user.UpdatedAt = DateTime.UtcNow;

        if (_context.Users != null)
        {
            try
            {
                var filter =
                    Builders<User>.Filter.Eq(
                        u => u.Id, user.Id);

                var result = await _context.Users.ReplaceOneAsync(
                    filter,
                    user,
                    cancellationToken: ct);

                if (result.MatchedCount > 0)
                {
                    _memoryUsers[user.Id] = user;
                    return;
                }

                throw new InvalidOperationException(
                    $"User '{user.Id}' was not found in MongoDB.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to update user in MongoDB.");
                throw;
            }
        }

        _memoryUsers[user.Id] = user;
    }

    // =========================================================
    // LOGIN SECURITY
    // =========================================================

    public async Task<int> IncrementFailedLoginAsync(
        string id,
        CancellationToken ct = default)
    {
        if (_context.Users != null)
        {
            var filter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(u => u.Id, id),
                Builders<User>.Filter.Eq(u => u.IsDeleted, false));

            var updated = await _context.Users.FindOneAndUpdateAsync(
                filter,
                Builders<User>.Update.Inc(
                    u => u.FailedLoginCount, 1),
                new FindOneAndUpdateOptions<User>
                {
                    ReturnDocument = ReturnDocument.After
                },
                ct);

            return updated?.FailedLoginCount ?? 0;
        }

        if (!_memoryUsers.TryGetValue(id, out var user)
            || user.IsDeleted)
        {
            return 0;
        }

        return Interlocked.Increment(
            ref user.FailedLoginCount);
    }

    public async Task LockAsync(
        string id,
        DateTime lockoutEnd,
        CancellationToken ct = default)
    {
        if (_context.Users != null)
        {
            await _context.Users.UpdateOneAsync(
                Builders<User>.Filter.Eq(u => u.Id, id),
                Builders<User>.Update
                    .Set(u => u.LockoutEnd, lockoutEnd)
                    .Set(u => u.FailedLoginCount, 0),
                cancellationToken: ct);

            return;
        }

        if (_memoryUsers.TryGetValue(id, out var user))
        {
            user.LockoutEnd = lockoutEnd;
            user.FailedLoginCount = 0;
        }
    }

    public async Task RecordSuccessfulLoginAsync(
        string id,
        DateTime now,
        string? newPasswordHash,
        CancellationToken ct = default)
    {
        if (_context.Users != null)
        {
            var update = Builders<User>.Update
                .Set(u => u.LastLoginAt, now)
                .Set(u => u.FailedLoginCount, 0)
                .Unset(u => u.LockoutEnd);

            if (newPasswordHash is not null)
            {
                update = update.Set(
                    u => u.PasswordHash, newPasswordHash);
            }

            await _context.Users.UpdateOneAsync(
                Builders<User>.Filter.Eq(u => u.Id, id),
                update,
                cancellationToken: ct);

            return;
        }

        if (_memoryUsers.TryGetValue(id, out var user))
        {
            user.LastLoginAt = now;
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;

            if (newPasswordHash is not null)
            {
                user.PasswordHash = newPasswordHash;
            }
        }
    }

    // =========================================================
    // FORGOT PASSWORD
    // =========================================================

    public async Task UpdatePasswordAsync(
        string id,
        string newPasswordHash,
        CancellationToken ct = default)
    {
        if (_context.Users != null)
        {
            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, newPasswordHash)
                .Set(u => u.FailedLoginCount, 0)
                .Unset(u => u.LockoutEnd);

            await _context.Users.UpdateOneAsync(
                Builders<User>.Filter.Eq(u => u.Id, id),
                update,
                cancellationToken: ct);

            return;
        }

        if (_memoryUsers.TryGetValue(id, out var user))
        {
            user.PasswordHash = newPasswordHash;
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
        }
    }
}
