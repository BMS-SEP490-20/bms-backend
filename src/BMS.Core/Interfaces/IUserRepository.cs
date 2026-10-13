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
}
