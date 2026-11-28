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
    public static readonly Collation CaseInsensitive =
        new("en", strength: CollationStrength.Secondary);

    /// <summary>
    /// Partial unique indexes on <c>accounts</c> enforcing unique Email (case-insensitive),
    /// PhoneNumber, and 12-digit CCCD (identityCardNumber) among active non-empty values.
    /// </summary>
    public static IEnumerable<CreateIndexModel<User>> Indexes()
    {
        var fb = Builders<User>.Filter;
        return
        [
            new(Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions<User>
                {
                    Name = "ux_accounts_email",
                    Unique = true,
                    Collation = CaseInsensitive,
                    PartialFilterExpression = fb.And(
                        fb.Eq(u => u.IsDeleted, false),
                        fb.Gt(u => u.Email, ""))
                }),
            new(Builders<User>.IndexKeys.Ascending(u => u.PhoneNumber),
                new CreateIndexOptions<User>
                {
                    Name = "ux_accounts_phone",
                    Unique = true,
                    PartialFilterExpression = fb.And(
                        fb.Eq(u => u.IsDeleted, false),
                        fb.Gt(u => u.PhoneNumber, ""))
                }),
            new(Builders<User>.IndexKeys.Ascending(u => u.IdentityCardNumber),
                new CreateIndexOptions<User>
                {
                    Name = "ux_accounts_cccd",
                    Unique = true,
                    PartialFilterExpression = fb.And(
                        fb.Eq(u => u.IsDeleted, false),
                        fb.Gt(u => u.IdentityCardNumber, ""))
                })
        ];
    }

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

    public async Task<User?> GetByIdAsync(
        string id,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmed = id.Trim();

        if (_context.Users != null)
        {
            try
            {
                var fb = Builders<User>.Filter;
                var filter = fb.And(
                    fb.Or(
                        fb.Eq(u => u.Id, trimmed),
                        fb.Eq(u => u.Username, trimmed),
                        fb.Eq(u => u.PatientCode, trimmed)
                    ),
                    fb.Eq(u => u.IsDeleted, false));

                var found = await _context.Users.Find(filter).FirstOrDefaultAsync(ct);
                if (found != null) return found;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query user by ID. Falling back to memory.");
            }
        }

        return _memoryUsers.Values.FirstOrDefault(u =>
            (string.Equals(u.Id, trimmed, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(u.Username, trimmed, StringComparison.OrdinalIgnoreCase)) &&
            !u.IsDeleted);
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
                        Builders<User>.Filter.Eq("Email", keyword),
                        Builders<User>.Filter.Eq("email", keyword),
                        Builders<User>.Filter.Eq("Username", keyword),
                        Builders<User>.Filter.Eq("username", keyword),
                        Builders<User>.Filter.Eq("PhoneNumber", keyword),
                        Builders<User>.Filter.Eq("phoneNumber", keyword),
                        Builders<User>.Filter.Eq("phone", keyword),
                        Builders<User>.Filter.Eq("identityCardNumber", keyword),
                        Builders<User>.Filter.Eq("IdentityCardNumber", keyword),
                        Builders<User>.Filter.Eq("patientCode", keyword),
                        Builders<User>.Filter.Eq("PatientCode", keyword)),
                    Builders<User>.Filter.Ne("isDeleted", true));

                var found = await _context.Users
                    .Find(filter, new FindOptions { Collation = CaseInsensitive })
                    .FirstOrDefaultAsync(ct);
                if (found != null) return found;
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
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 u.PhoneNumber, keyword,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 u.IdentityCardNumber, keyword,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 u.PatientCode, keyword,
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
                    Builders<User>.Filter.Or(
                        Builders<User>.Filter.Eq("Email", keyword),
                        Builders<User>.Filter.Eq("email", keyword)),
                    Builders<User>.Filter.Ne("isDeleted", true));

                var found = await _context.Users
                    .Find(filter, new FindOptions { Collation = CaseInsensitive })
                    .FirstOrDefaultAsync(ct);
                if (found != null) return found;
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

    public async Task<User?> FindByPhoneAsync(
        string phoneNumber,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        var trimmed = phoneNumber.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        var normalized = digits.StartsWith("84") && digits.Length > 2 ? "0" + digits[2..] : digits;
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { trimmed };
        if (!string.IsNullOrEmpty(digits)) candidates.Add(digits);
        if (!string.IsNullOrEmpty(normalized))
        {
            candidates.Add(normalized);
            if (normalized.StartsWith("0") && normalized.Length >= 10)
            {
                candidates.Add("84" + normalized[1..]);
                candidates.Add("+84" + normalized[1..]);
            }
        }

        var candidateList = candidates.ToArray();

        if (_context.Users != null)
        {
            try
            {
                var fb = Builders<User>.Filter;
                var filter = fb.And(
                    fb.Or(
                        fb.In("PhoneNumber", candidateList),
                        fb.In("phoneNumber", candidateList),
                        fb.In("phone", candidateList)),
                    fb.Ne("isDeleted", true));

                var found = await _context.Users.Find(filter).FirstOrDefaultAsync(ct);
                if (found != null) return found;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query user by phone. Falling back to memory.");
            }
        }

        return _memoryUsers.Values.FirstOrDefault(u =>
            !u.IsDeleted &&
            !string.IsNullOrWhiteSpace(u.PhoneNumber) &&
            candidates.Contains(u.PhoneNumber.Trim()));
    }

    public async Task<User?> FindByIdentityCardAsync(
        string identityCardNumber,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identityCardNumber))
            return null;

        var keyword = identityCardNumber.Trim();

        if (_context.Users != null)
        {
            try
            {
                var fb = Builders<User>.Filter;
                var filter = fb.And(
                    fb.Or(
                        fb.Eq("identityCardNumber", keyword),
                        fb.Eq("IdentityCardNumber", keyword)),
                    fb.Ne("isDeleted", true));

                var found = await _context.Users.Find(filter).FirstOrDefaultAsync(ct);
                if (found != null) return found;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query user by CCCD. Falling back to memory.");
            }
        }

        return _memoryUsers.Values.FirstOrDefault(u =>
            !u.IsDeleted &&
            string.Equals(u.IdentityCardNumber?.Trim(), keyword, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<long> CountUsersByRoleIdAsync(
        string roleId,
        CancellationToken ct = default)
    {
        if (_context.Users != null)
        {
            try
            {
                var roleFilters = new List<FilterDefinition<User>>();
                if (int.TryParse(roleId, out var parsedIntRole))
                {
                    roleFilters.Add(Builders<User>.Filter.Eq(u => u.RoleId, parsedIntRole));
                }
                else if (Enum.TryParse<UserRole>(roleId, true, out var parsedRole))
                {
                    roleFilters.Add(Builders<User>.Filter.Eq(u => u.RoleId, (int)parsedRole));
                }

                if (roleFilters.Count == 0)
                {
                    return 0;
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
            ((int.TryParse(roleId, out var parsedInt) && u.RoleId == parsedInt) ||
             string.Equals(
                 u.Role.ToString(),
                 roleId,
                 StringComparison.OrdinalIgnoreCase)));
    }

    public async Task CreateAsync(
        User user,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id) || !ObjectId.TryParse(user.Id, out _))
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

    public async Task<int> IncrementFailedLoginAsync(
        string id,
        CancellationToken ct = default)
    {
        if (_context.Users != null && !string.IsNullOrWhiteSpace(id))
        {
            var filter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(u => u.Id, id),
                Builders<User>.Filter.Ne("isDeleted", true));

            var updated = await _context.Users.FindOneAndUpdateAsync(
                filter,
                Builders<User>.Update.Inc(u => u.FailedLoginCount, 1),
                new FindOneAndUpdateOptions<User>
                {
                    ReturnDocument = ReturnDocument.After
                },
                ct);

            if (updated != null)
                return updated.FailedLoginCount;
        }

        if (!_memoryUsers.TryGetValue(id, out var user)
            || user.IsDeleted)
        {
            return 0;
        }

        user.FailedLoginCount++;
        return user.FailedLoginCount;
    }

    public async Task LockAsync(
        string id,
        DateTime lockoutEnd,
        CancellationToken ct = default)
    {
        if (_context.Users != null && !string.IsNullOrWhiteSpace(id))
        {
            await _context.Users.UpdateOneAsync(
                Builders<User>.Filter.Eq(u => u.Id, id),
                Builders<User>.Update
                    .Set(u => u.LockoutEnd, lockoutEnd)
                    .Set(u => u.FailedLoginCount, 0),
                cancellationToken: ct);
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
        if (_context.Users != null && !string.IsNullOrWhiteSpace(id))
        {
            var update = Builders<User>.Update
                .Set(u => u.LastLoginAt, now)
                .Set(u => u.FailedLoginCount, 0)
                .Unset(u => u.LockoutEnd);

            if (newPasswordHash is not null)
            {
                update = update
                    .Set(u => u.Password, newPasswordHash)
                    .Set(u => u.PasswordHash, newPasswordHash);
            }

            await _context.Users.UpdateOneAsync(
                Builders<User>.Filter.Eq(u => u.Id, id),
                update,
                cancellationToken: ct);
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

    public async Task UpdatePasswordAsync(
        string id,
        string newPasswordHash,
        CancellationToken ct = default)
    {
        if (_context.Users != null && !string.IsNullOrWhiteSpace(id))
        {
            var update = Builders<User>.Update
                .Set(u => u.Password, newPasswordHash)
                .Set(u => u.PasswordHash, newPasswordHash)
                .Set(u => u.UpdatedAt, DateTime.UtcNow)
                .Set(u => u.FailedLoginCount, 0)
                .Unset(u => u.LockoutEnd)
                .Unset(u => u.PasswordResetOtp)
                .Unset(u => u.PasswordResetExpiresAt);

            await _context.Users.UpdateOneAsync(
                Builders<User>.Filter.Eq(u => u.Id, id),
                update,
                cancellationToken: ct);
        }

        if (_memoryUsers.TryGetValue(id, out var user))
        {
            user.PasswordHash = newPasswordHash;
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
            user.PasswordResetOtp = null;
            user.PasswordResetExpiresAt = null;
        }
    }
}
