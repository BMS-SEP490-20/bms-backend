using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data repository contract for clinical and administrative user profile operations.
/// Conforms to: Repository Pattern & Clean Architecture.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Retrieves active user entity by MongoDB ObjectId string identifier.
    /// </summary>
    Task<User?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves user entity matching email or username.
    /// </summary>
    Task<User?> GetByEmailOrUsernameAsync(string identifier, CancellationToken ct = default);

    /// <summary>
    /// Counts total active users assigned to a specified role identifier (Used for BR-ROLE-02 safety checks).
    /// </summary>
    Task<long> CountUsersByRoleIdAsync(string roleId, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new user document into the repository.
    /// </summary>
    Task CreateAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing user document.
    /// </summary>
    Task UpdateAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Retrieves user entity matching email specifically.
    /// </summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Retrieves user entity matching unique ID.
    /// </summary>
    Task<User?> FindByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Updates password hash for the specified user.
    /// </summary>
    Task UpdatePasswordAsync(string id, string newPasswordHash, CancellationToken ct = default);

    /// <summary>
    /// Finds a user by username or email (case-insensitive).
    /// </summary>
    Task<User?> FindByLoginAsync(string usernameOrEmail, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments failed login attempts and returns the updated count.
    /// </summary>
    Task<int> IncrementFailedLoginAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Locks a user account until the specified UTC timestamp and resets the failed login counter.
    /// </summary>
    Task LockAsync(string id, DateTime lockoutEnd, CancellationToken ct = default);

    /// <summary>
    /// Records a successful login, resetting lockout state and optionally upgrading the password hash.
    /// </summary>
    Task RecordSuccessfulLoginAsync(string id, DateTime now, string? newPasswordHash, CancellationToken ct = default);
}
