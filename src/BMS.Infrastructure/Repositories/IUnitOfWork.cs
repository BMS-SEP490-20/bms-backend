namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Unit of work interface coordinating repository operations and transactional boundaries in MongoDB.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Begins a multi-document transaction (if supported by MongoDB replica set).
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits all pending transactional changes.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts and rolls back all transactional operations in the current session.
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
