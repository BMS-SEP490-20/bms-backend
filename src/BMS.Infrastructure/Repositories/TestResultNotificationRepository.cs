using System.Collections.Concurrent;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Test Result Notifications with MongoDB persistence and resilient in-memory fallback (UC60 / BR-182).
/// Conforms to: Production Engineering Standards, Clean MVC Architecture & AGENTS-CODE.md.
/// </summary>
public class TestResultNotificationRepository : ITestResultNotificationRepository
{
    private readonly MongoDbContext _context;
    private readonly ILogger<TestResultNotificationRepository> _logger;

    private static readonly ConcurrentDictionary<string, TestResultNotification> _memoryStore = new();
    private static bool _seeded = false;
    private static readonly object _seedLock = new();

    public TestResultNotificationRepository(
        MongoDbContext context,
        ILogger<TestResultNotificationRepository> logger)
    {
        _context = context;
        _logger = logger;

        EnsureSeedData();
    }

    private void EnsureSeedData()
    {
        if (_seeded) return;
        lock (_seedLock)
        {
            if (_seeded) return;

            var seeds = new List<TestResultNotification>
            {
                new()
                {
                    Id = "671600000000000000000001",
                    NotificationId = "NOTIF-261021-001",
                    TestOrderId = "ORD-20261021-001",
                    SpecimenBarcode = "SMP-261021-001",
                    PatientId = "USR-PAT-01",
                    PatientName = "Nguyễn Văn An",
                    PatientPhone = "0912345678",
                    PatientEmail = "an.nguyen@gmail.com",
                    Title = "Kết quả xét nghiệm Tổng Quát Định Kỳ đã hoàn thành",
                    Message = "Hồ sơ xét nghiệm của quý khách (Mã đơn: ORD-20261021-001) đã được duyệt y khoa bởi Bác sĩ chuyên khoa. Các chỉ số đều nằm trong giới hạn an toàn.",
                    Channel = TestResultNotificationChannel.All,
                    Priority = NotificationPriority.Normal,
                    Status = NotificationDeliveryStatus.Delivered,
                    IsCriticalPanicValue = false,
                    CriticalParameters = [],
                    RequiresImmediateAction = false,
                    ClinicalRecommendation = "Tái khám định kỳ sau 6 tháng. Giữ chế độ ăn uống lành mạnh.",
                    TestPackageName = "Gói Xét Nghiệm Tổng Quát Toàn Diện",
                    SentAt = DateTime.UtcNow.AddHours(-3),
                    DeliveredAt = DateTime.UtcNow.AddHours(-2).AddMinutes(58),
                    ReadAt = DateTime.UtcNow.AddHours(-1),
                    CreatedAt = DateTime.UtcNow.AddHours(-3),
                    CreatedBy = "BS. Nguyễn Văn An"
                },
                new()
                {
                    Id = "671600000000000000000002",
                    NotificationId = "NOTIF-261021-002",
                    TestOrderId = "ORD-20261021-002",
                    SpecimenBarcode = "SMP-261021-002",
                    PatientId = "USR-PAT-02",
                    PatientName = "Trần Thị Bích",
                    PatientPhone = "0987654321",
                    PatientEmail = "bich.tran@gmail.com",
                    Title = "[CẢNH BÁO NGUY HIỂM] Chỉ số Glucose & HbA1c vượt ngưỡng hoảng loạn",
                    Message = "CẢNH BÁO Y KHOA: Kết quả xét nghiệm đường huyết của bệnh nhân Trần Thị Bích đạt mức 29.4 mmol/L (Ngưỡng hoảng loạn > 25.0 mmol/L). Yêu cầu liên hệ khẩn cấp với bác sĩ điều trị hoặc đến ngay phòng cấp cứu gần nhất.",
                    Channel = TestResultNotificationChannel.All,
                    Priority = NotificationPriority.Critical,
                    Status = NotificationDeliveryStatus.Delivered,
                    IsCriticalPanicValue = true,
                    CriticalParameters =
                    [
                        "Glucose huyết tương: 29.4 mmol/L [Báo động nguy kịch - Chuẩn 3.9 - 6.4]",
                        "HbA1c: 12.8% [Rất cao - Nguy cơ nhiễm toan ceton do đái tháo đường]"
                    ],
                    RequiresImmediateAction = true,
                    ClinicalRecommendation = "Nhập viện cấp cứu khẩn cấp để bù dịch và truyền Insulin tĩnh mạch kiểm soát toan chuyển hóa.",
                    TestPackageName = "Gói Đánh Giá Rối Loạn Đường Huyết Nâng Cao",
                    SentAt = DateTime.UtcNow.AddMinutes(-45),
                    DeliveredAt = DateTime.UtcNow.AddMinutes(-44),
                    ReadAt = null,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-45),
                    CreatedBy = "KTV. Lê Thị Bình"
                },
                new()
                {
                    Id = "671600000000000000000003",
                    NotificationId = "NOTIF-261021-003",
                    TestOrderId = "ORD-20261021-003",
                    SpecimenBarcode = "SMP-261021-003",
                    PatientId = "USR-PAT-03",
                    PatientName = "Lê Hoàng Nam",
                    PatientPhone = "0933112233",
                    PatientEmail = "nam.le@gmail.com",
                    Title = "Thông báo kết quả xét nghiệm Chức Năng Gan & Thận",
                    Message = "Kết quả xét nghiệm bộ chỉ số Men gan (AST, ALT) và Chức năng thận (Ure, Creatinine) đã sẵn sàng để tra cứu trên ứng dụng BMS Health.",
                    Channel = TestResultNotificationChannel.Push,
                    Priority = NotificationPriority.High,
                    Status = NotificationDeliveryStatus.Sent,
                    IsCriticalPanicValue = false,
                    CriticalParameters = [],
                    RequiresImmediateAction = false,
                    ClinicalRecommendation = "Chỉ số men gan AST hơi tăng nhẹ (52 U/L), khuyến nghị hạn chế rượu bia và bổ sung nước.",
                    TestPackageName = "Gói Xét Nghiệm Chức Năng Gan & Thận",
                    SentAt = DateTime.UtcNow.AddMinutes(-15),
                    DeliveredAt = DateTime.UtcNow.AddMinutes(-14),
                    ReadAt = null,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-15),
                    CreatedBy = "Hệ thống tự động"
                }
            };

            foreach (var s in seeds)
            {
                _memoryStore[s.NotificationId] = s;
                _memoryStore[s.Id] = s;
            }

            if (_context.TestResultNotifications != null)
            {
                try
                {
                    var count = _context.TestResultNotifications.CountDocuments(Builders<TestResultNotification>.Filter.Empty);
                    if (count == 0)
                    {
                        _context.TestResultNotifications.InsertMany(seeds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TestResultNotificationRepository] Failed to seed into MongoDB, continuing with in-memory");
                }
            }

            _seeded = true;
            _logger.LogInformation("[TestResultNotificationRepository] Seeded {Count} test result notifications", seeds.Count);
        }
    }

    public async Task<TestResultNotification?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        if (_context.TestResultNotifications != null)
        {
            try
            {
                var filter = Builders<TestResultNotification>.Filter.Or(
                    Builders<TestResultNotification>.Filter.Eq(x => x.Id, id),
                    Builders<TestResultNotification>.Filter.Eq(x => x.NotificationId, id));

                var found = await _context.TestResultNotifications.Find(filter).FirstOrDefaultAsync(ct);
                if (found != null) return found;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TestResultNotificationRepository] Failed Mongo lookup for {Id}, falling back to memory", id);
            }
        }

        _memoryStore.TryGetValue(id, out var memoryNotif);
        return memoryNotif;
    }

    public async Task<List<TestResultNotification>> GetByPatientIdAsync(string patientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientId)) return [];

        if (_context.TestResultNotifications != null)
        {
            try
            {
                var filter = Builders<TestResultNotification>.Filter.Eq(x => x.PatientId, patientId);
                var list = await _context.TestResultNotifications
                    .Find(filter)
                    .SortByDescending(x => x.CreatedAt)
                    .ToListAsync(ct);

                if (list.Count > 0) return list;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TestResultNotificationRepository] Failed Mongo query for patient {PatientId}", patientId);
            }
        }

        return _memoryStore.Values
            .Where(x => string.Equals(x.PatientId, patientId, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(x => x.NotificationId)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    public async Task<(List<TestResultNotification> Items, int TotalCount)> GetPagedAsync(
        string? patientId,
        string? testOrderId,
        TestResultNotificationChannel? channel,
        NotificationPriority? priority,
        NotificationDeliveryStatus? status,
        bool? isCritical,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        if (_context.TestResultNotifications != null)
        {
            try
            {
                var builder = Builders<TestResultNotification>.Filter;
                var filters = new List<FilterDefinition<TestResultNotification>>();

                if (!string.IsNullOrWhiteSpace(patientId))
                    filters.Add(builder.Eq(x => x.PatientId, patientId.Trim()));

                if (!string.IsNullOrWhiteSpace(testOrderId))
                    filters.Add(builder.Eq(x => x.TestOrderId, testOrderId.Trim()));

                if (channel.HasValue && channel.Value != TestResultNotificationChannel.All)
                    filters.Add(builder.Eq(x => x.Channel, channel.Value));

                if (priority.HasValue)
                    filters.Add(builder.Eq(x => x.Priority, priority.Value));

                if (status.HasValue)
                    filters.Add(builder.Eq(x => x.Status, status.Value));

                if (isCritical.HasValue)
                    filters.Add(builder.Eq(x => x.IsCriticalPanicValue, isCritical.Value));

                if (fromDate.HasValue)
                    filters.Add(builder.Gte(x => x.CreatedAt, fromDate.Value));

                if (toDate.HasValue)
                    filters.Add(builder.Lte(x => x.CreatedAt, toDate.Value));

                var combined = filters.Count > 0 ? builder.And(filters) : builder.Empty;

                var total = (int)await _context.TestResultNotifications.CountDocumentsAsync(combined, cancellationToken: ct);
                var items = await _context.TestResultNotifications
                    .Find(combined)
                    .SortByDescending(x => x.CreatedAt)
                    .Skip((pageNumber - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync(ct);

                if (total > 0 || items.Count > 0)
                {
                    return (items, total);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TestResultNotificationRepository] Falling back to memory store for paged query");
            }
        }

        var query = _memoryStore.Values.DistinctBy(x => x.NotificationId).AsQueryable();

        if (!string.IsNullOrWhiteSpace(patientId))
            query = query.Where(x => x.PatientId.Equals(patientId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(testOrderId))
            query = query.Where(x => x.TestOrderId.Equals(testOrderId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (channel.HasValue && channel.Value != TestResultNotificationChannel.All)
            query = query.Where(x => x.Channel == channel.Value);

        if (priority.HasValue)
            query = query.Where(x => x.Priority == priority.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        if (isCritical.HasValue)
            query = query.Where(x => x.IsCriticalPanicValue == isCritical.Value);

        if (fromDate.HasValue)
            query = query.Where(x => x.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(x => x.CreatedAt <= toDate.Value);

        var memoryTotal = query.Count();
        var memoryItems = query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (memoryItems, memoryTotal);
    }

    public async Task<TestResultNotification> AddAsync(TestResultNotification notification, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(notification.NotificationId))
        {
            notification.NotificationId = $"NOTIF-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }

        if (_context.TestResultNotifications != null)
        {
            try
            {
                await _context.TestResultNotifications.InsertOneAsync(notification, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TestResultNotificationRepository] Failed Mongo insert, cached in memory only");
            }
        }

        _memoryStore[notification.NotificationId] = notification;
        if (!string.IsNullOrWhiteSpace(notification.Id))
        {
            _memoryStore[notification.Id] = notification;
        }

        return notification;
    }

    public async Task<TestResultNotification> UpdateAsync(TestResultNotification notification, CancellationToken ct = default)
    {
        if (_context.TestResultNotifications != null)
        {
            try
            {
                var filter = Builders<TestResultNotification>.Filter.Eq(x => x.NotificationId, notification.NotificationId);
                await _context.TestResultNotifications.ReplaceOneAsync(filter, notification, new ReplaceOptions { IsUpsert = true }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TestResultNotificationRepository] Failed Mongo replace, updating memory only");
            }
        }

        _memoryStore[notification.NotificationId] = notification;
        if (!string.IsNullOrWhiteSpace(notification.Id))
        {
            _memoryStore[notification.Id] = notification;
        }

        return notification;
    }

    public async Task<bool> MarkAsReadAsync(string id, DateTime readAt, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity == null) return false;

        entity.Status = NotificationDeliveryStatus.Read;
        entity.ReadAt = readAt;

        await UpdateAsync(entity, ct);
        return true;
    }

    public async Task<int> GetUnreadCountAsync(string patientId, CancellationToken ct = default)
    {
        var all = await GetByPatientIdAsync(patientId, ct);
        return all.Count(x => x.Status != NotificationDeliveryStatus.Read);
    }

    public async Task<Dictionary<string, int>> GetNotificationStatsAsync(CancellationToken ct = default)
    {
        var (items, total) = await GetPagedAsync(null, null, null, null, null, null, null, null, 1, 1000, ct);

        return new Dictionary<string, int>
        {
            ["Total"] = total,
            ["Delivered"] = items.Count(x => x.Status == NotificationDeliveryStatus.Delivered || x.Status == NotificationDeliveryStatus.Read),
            ["CriticalPanic"] = items.Count(x => x.IsCriticalPanicValue),
            ["Pending"] = items.Count(x => x.Status == NotificationDeliveryStatus.Pending),
            ["Failed"] = items.Count(x => x.Status == NotificationDeliveryStatus.Failed)
        };
    }
}
