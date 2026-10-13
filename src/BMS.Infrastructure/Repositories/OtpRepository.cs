using System.Collections.Concurrent;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Implements the OTP Repository interacting with MongoDB collection "otp_tokens".
/// Includes resilient in-memory fallback support so that developers and tests can run reliably offline.
/// </summary>
public class OtpRepository : IOtpRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<OtpRepository> _logger;

    // ConcurrentDictionary acting as in-memory fallback when MongoDB service is unreachable
    private static readonly ConcurrentDictionary<string, OtpTokenEntity> _memoryFallback = new();

    public OtpRepository(MongoDbContext context, ILogger<OtpRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Persists a new OTP token into MongoDB or memory fallback.
    /// </summary>
    public async Task CreateAsync(OtpTokenEntity entity, CancellationToken ct = default)
    {
        if (_context.OtpTokens != null)
        {
            try
            {
                await _context.OtpTokens.InsertOneAsync(entity, cancellationToken: ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist OTP to MongoDB. Falling back to in-memory store.");
            }
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            entity.Id = Guid.NewGuid().ToString("N");
        }
        _memoryFallback[entity.Id] = entity;
    }

    /// <summary>
    /// Retrieves the most recent active and unexpired OTP for a given recipient and purpose.
    /// </summary>
    public async Task<OtpTokenEntity?> GetLatestActiveByRecipientAsync(
        string recipient,
        string purpose,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        if (_context.OtpTokens != null)
        {
            try
            {
                var filter = Builders<OtpTokenEntity>.Filter.And(
                    Builders<OtpTokenEntity>.Filter.Eq(x => x.Recipient, recipient),
                    Builders<OtpTokenEntity>.Filter.Eq(x => x.Purpose, purpose),
                    Builders<OtpTokenEntity>.Filter.Eq(x => x.IsUsed, false),
                    Builders<OtpTokenEntity>.Filter.Gt(x => x.ExpiresAt, now)
                );

                var sort = Builders<OtpTokenEntity>.Sort.Descending(x => x.CreatedAt);

                return await _context.OtpTokens
                    .Find(filter)
                    .Sort(sort)
                    .FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query MongoDB for OTP. Falling back to in-memory store.");
            }
        }

        return _memoryFallback.Values
            .Where(x => x.Recipient == recipient &&
                        x.Purpose == purpose &&
                        !x.IsUsed &&
                        x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// Updates the state of an existing OTP token entity.
    /// </summary>
    public async Task UpdateAsync(OtpTokenEntity entity, CancellationToken ct = default)
    {
        if (_context.OtpTokens != null)
        {
            try
            {
                var filter = Builders<OtpTokenEntity>.Filter.Eq(x => x.Id, entity.Id);
                await _context.OtpTokens.ReplaceOneAsync(filter, entity, cancellationToken: ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update OTP on MongoDB.");
            }
        }

        _memoryFallback[entity.Id] = entity;
    }

    /// <summary>
    /// Invalidates all previously issued active OTPs for the recipient and purpose.
    /// </summary>
    public async Task InvalidatePreviousOtpsAsync(
        string recipient,
        string purpose,
        CancellationToken ct = default)
    {
        if (_context.OtpTokens != null)
        {
            try
            {
                var filter = Builders<OtpTokenEntity>.Filter.And(
                    Builders<OtpTokenEntity>.Filter.Eq(x => x.Recipient, recipient),
                    Builders<OtpTokenEntity>.Filter.Eq(x => x.Purpose, purpose),
                    Builders<OtpTokenEntity>.Filter.Eq(x => x.IsUsed, false)
                );

                var update = Builders<OtpTokenEntity>.Update.Set(x => x.IsUsed, true);
                await _context.OtpTokens.UpdateManyAsync(filter, update, cancellationToken: ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to invalidate previous OTPs on MongoDB.");
            }
        }

        foreach (var item in _memoryFallback.Values.Where(x => x.Recipient == recipient && x.Purpose == purpose && !x.IsUsed))
        {
            item.IsUsed = true;
        }
    }

    /// <summary>
    /// Counts OTP requests within the specified sliding window for Rate Limiting.
    /// </summary>
    public async Task<int> CountRecentRequestsAsync(
        string recipient,
        TimeSpan window,
        CancellationToken ct = default)
    {
        var sinceTime = DateTime.UtcNow.Subtract(window);

        if (_context.OtpTokens != null)
        {
            try
            {
                var filter = Builders<OtpTokenEntity>.Filter.And(
                    Builders<OtpTokenEntity>.Filter.Eq(x => x.Recipient, recipient),
                    Builders<OtpTokenEntity>.Filter.Gte(x => x.CreatedAt, sinceTime)
                );

                var count = await _context.OtpTokens.CountDocumentsAsync(filter, cancellationToken: ct);
                return (int)count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to count recent requests on MongoDB.");
            }
        }

        return _memoryFallback.Values
            .Count(x => x.Recipient == recipient && x.CreatedAt >= sinceTime);
    }
}
