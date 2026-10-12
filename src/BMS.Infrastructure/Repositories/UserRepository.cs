using System.Collections.Concurrent;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Implements the User Repository interacting with MongoDB collection "users".
/// Includes resilient in-memory fallback support for offline development and testing.
/// Conforms to: Repository Pattern & Clean Architecture.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<UserRepository> _logger;

    // In-memory fallback dictionary
    private static readonly ConcurrentDictionary<string, User> _memoryUsers = new();

    public UserRepository(MongoDbContext context, ILogger<UserRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves user by MongoDB ObjectId or memory fallback.
    /// </summary>
    public async Task<User?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (_context.Users != null)
        {
            try
            {
                var filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.Eq(u => u.Id, id),
                    Builders<User>.Filter.Eq(u => u.IsDeleted, false)
                );
                return await _context.Users.Find(filter).FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query user from MongoDB. Falling back to in-memory store.");
            }
        }

        _memoryUsers.TryGetValue(id, out var memoryUser);
        return memoryUser is { IsDeleted: false } ? memoryUser : null;
    }

    /// <summary>
    /// Retrieves user by email or username.
    /// </summary>
    public async Task<User?> GetByEmailOrUsernameAsync(string identifier, CancellationToken ct = default)
    {
        var lowerId = identifier.ToLowerInvariant().Trim();

        if (_context.Users != null)
        {
            try
            {
                var filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.Or(
                        Builders<User>.Filter.Eq(u => u.Email, lowerId),
                        Builders<User>.Filter.Eq(u => u.Username, lowerId)
                    ),
                    Builders<User>.Filter.Eq(u => u.IsDeleted, false)
                );
                return await _context.Users.Find(filter).FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query user by identifier from MongoDB. Falling back to memory store.");
            }
        }

        return _memoryUsers.Values.FirstOrDefault(u =>
            !u.IsDeleted &&
            (u.Email.Equals(lowerId, StringComparison.OrdinalIgnoreCase) ||
             u.Username.Equals(lowerId, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Counts active users belonging to a specific role.
    /// </summary>
    public async Task<long> CountUsersByRoleIdAsync(string roleId, CancellationToken ct = default)
    {
        if (_context.Users != null)
        {
            try
            {
                var roleParsed = Enum.TryParse<UserRole>(roleId, true, out var parsedRole);
                var roleFilters = new List<FilterDefinition<User>>
                {
                    Builders<User>.Filter.Eq(u => u.RoleId, roleId)
                };

                if (roleParsed)
                {
                    roleFilters.Add(Builders<User>.Filter.Eq(u => u.Role, parsedRole));
                }

                var filter = Builders<User>.Filter.And(
                    Builders<User>.Filter.Or(roleFilters),
                    Builders<User>.Filter.Eq(u => u.IsDeleted, false)
                );
                return await _context.Users.CountDocumentsAsync(filter, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to count users by role from MongoDB. Falling back to memory store.");
            }
        }

        return _memoryUsers.Values.Count(u =>
            !u.IsDeleted &&
            (u.RoleId == roleId || u.Role.ToString().Equals(roleId, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Inserts a new user document into MongoDB or memory store.
    /// </summary>
    public async Task CreateAsync(User user, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.Id))
        {
            user.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
        }

        if (_context.Users != null)
        {
            try
            {
                await _context.Users.InsertOneAsync(user, cancellationToken: ct);
                _memoryUsers[user.Id] = user;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert user into MongoDB. Storing in memory fallback.");
            }
        }

        _memoryUsers[user.Id] = user;
    }

    /// <summary>
    /// Updates an existing user document.
    /// </summary>
    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        user.UpdatedAt = DateTime.UtcNow;

        if (_context.Users != null)
        {
            try
            {
                var filter = Builders<User>.Filter.Eq(u => u.Id, user.Id);
                await _context.Users.ReplaceOneAsync(filter, user, cancellationToken: ct);
                _memoryUsers[user.Id] = user;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update user in MongoDB. Storing in memory fallback.");
            }
        }

        _memoryUsers[user.Id] = user;
    }
}
