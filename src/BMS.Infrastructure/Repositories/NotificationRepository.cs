using BMS.Core.Entities;
using BMS.Core.Interfaces;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories
{
    /// <summary>
    /// MongoDB implementation of <see cref="INotificationRepository"/> over <c>notifications</c> (I3-034).
    /// Replaces the legacy EF Core implementation.
    /// </summary>
    public sealed class NotificationRepository : INotificationRepository
    {
        private const string BroadcastUser = "ALL";
        private const string BroadcastRole = "All";
        private readonly IMongoCollection<Notification> _notifications;

        /// <summary>Creates the repository bound to <c>notifications</c>.</summary>
        public NotificationRepository(IMongoDatabase database)
        {
            _notifications = database.GetCollection<Notification>("notifications");
        }

        private static FilterDefinitionBuilder<Notification> F => Builders<Notification>.Filter;
        private static FilterDefinition<Notification> ForUser(string userId) => F.In(n => n.UserId, new[] { userId, BroadcastUser });

        private async Task<IReadOnlyList<Notification>> ListAsync(FilterDefinition<Notification> filter, CancellationToken ct) =>
            await _notifications.Find(filter).SortByDescending(n => n.CreatedAt).Limit(INotificationRepository.MaxResults).ToListAsync(ct);

        /// <inheritdoc />
        public Task<IReadOnlyList<Notification>> GetByUserIdAsync(string userId, CancellationToken ct = default) =>
            ListAsync(ForUser(userId), ct);

        /// <inheritdoc />
        public Task<IReadOnlyList<Notification>> GetByRoleAsync(string role, CancellationToken ct = default) =>
            ListAsync(F.In(n => n.Role, new[] { role, BroadcastRole }), ct);

        /// <inheritdoc />
        public Task<IReadOnlyList<Notification>> GetByUserAndRoleAsync(string userId, string? role, CancellationToken ct = default)
        {
            var filter = string.IsNullOrWhiteSpace(role)
                ? ForUser(userId)
                : ForUser(userId) | F.In(n => n.Role, new[] { role.Trim(), BroadcastRole });
            return ListAsync(filter, ct);
        }

        /// <inheritdoc />
        public async Task<Notification?> GetByIdAsync(string id, CancellationToken ct = default) =>
            await _notifications.Find(F.Eq(n => n.NotificationId, id)).FirstOrDefaultAsync(ct);

        /// <inheritdoc />
        public Task AddAsync(Notification entity, CancellationToken ct = default) =>
            _notifications.InsertOneAsync(entity, cancellationToken: ct);

        /// <inheritdoc />
        public async Task<bool> MarkAsReadAsync(string id, DateTime readAt, CancellationToken ct = default)
        {
            var result = await _notifications.UpdateOneAsync(F.Eq(n => n.NotificationId, id),
                Builders<Notification>.Update.Set(n => n.IsRead, true).Set(n => n.ReadAt, readAt), cancellationToken: ct);
            return result.MatchedCount > 0;
        }

        /// <inheritdoc />
        public async Task<long> MarkAllAsReadAsync(string userId, DateTime readAt, CancellationToken ct = default)
        {
            // Only notifications owned by the user: marking a broadcast ("ALL") read would hide it for everyone.
            var result = await _notifications.UpdateManyAsync(F.Eq(n => n.UserId, userId) & F.Eq(n => n.IsRead, false),
                Builders<Notification>.Update.Set(n => n.IsRead, true).Set(n => n.ReadAt, readAt), cancellationToken: ct);
            return result.ModifiedCount;
        }
    }
}
