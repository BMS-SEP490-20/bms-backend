using System.Collections.Concurrent;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Triển khai kho dữ liệu OTP tương tác với MongoDB collection "otp_tokens".
/// Tích hợp cơ chế In-Memory Fallback tự động khi chạy kiểm thử mà chưa có dịch vụ MongoDB cục bộ.
/// </summary>
public class OtpRepository : IOtpRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<OtpRepository> _logger;

    // Bộ nhớ đệm ConcurrentDictionary đóng vai trò fallback khi MongoDB chưa sẵn sàng
    private static readonly ConcurrentDictionary<string, OtpTokenEntity> _memoryFallback = new();

    public OtpRepository(MongoDbContext context, ILogger<OtpRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lưu thực thể OTP mới vào MongoDB hoặc Fallback cache.
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
                _logger.LogWarning(ex, "Lỗi khi ghi OTP vào MongoDB. Chuyển sang lưu trữ Fallback bộ nhớ tạm.");
            }
        }

        if (string.IsNullOrEmpty(entity.Id))
        {
            entity.Id = Guid.NewGuid().ToString("N");
        }
        _memoryFallback[entity.Id] = entity;
    }

    /// <summary>
    /// Lấy bản ghi OTP mới nhất chưa sử dụng và chưa hết hạn của người nhận theo mục đích.
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
                _logger.LogWarning(ex, "Lỗi khi truy vấn OTP từ MongoDB. Tìm kiếm từ Fallback bộ nhớ tạm.");
            }
        }

        // Truy vấn từ Memory Fallback
        return _memoryFallback.Values
            .Where(x => x.Recipient == recipient &&
                        x.Purpose == purpose &&
                        !x.IsUsed &&
                        x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// Cập nhật trạng thái của bản ghi OTP (số lần thử, đánh dấu đã sử dụng).
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
                _logger.LogWarning(ex, "Lỗi khi cập nhật OTP trên MongoDB.");
            }
        }

        _memoryFallback[entity.Id] = entity;
    }

    /// <summary>
    /// Vô hiệu hóa tất cả các mã OTP còn hạn trước đó của người nhận để tránh tồn tại nhiều mã cùng lúc.
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
                _logger.LogWarning(ex, "Lỗi khi vô hiệu hóa OTP cũ trên MongoDB.");
            }
        }

        foreach (var item in _memoryFallback.Values.Where(x => x.Recipient == recipient && x.Purpose == purpose && !x.IsUsed))
        {
            item.IsUsed = true;
        }
    }

    /// <summary>
    /// Đếm số lần yêu cầu gửi OTP trong khoảng thời gian xác định (phục vụ Rate Limiting).
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
                _logger.LogWarning(ex, "Lỗi khi đếm yêu cầu gần đây trên MongoDB.");
            }
        }

        return _memoryFallback.Values
            .Count(x => x.Recipient == recipient && x.CreatedAt >= sinceTime);
    }
}
