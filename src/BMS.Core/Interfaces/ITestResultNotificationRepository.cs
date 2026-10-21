using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for Test Result Notifications persistence and querying (UC60 / BR-182).
/// </summary>
public interface ITestResultNotificationRepository
{
    Task<TestResultNotification?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<List<TestResultNotification>> GetByPatientIdAsync(string patientId, CancellationToken ct = default);

    Task<(List<TestResultNotification> Items, int TotalCount)> GetPagedAsync(
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
        CancellationToken ct = default);

    Task<TestResultNotification> AddAsync(TestResultNotification notification, CancellationToken ct = default);

    Task<TestResultNotification> UpdateAsync(TestResultNotification notification, CancellationToken ct = default);

    Task<bool> MarkAsReadAsync(string id, DateTime readAt, CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(string patientId, CancellationToken ct = default);

    Task<Dictionary<string, int>> GetNotificationStatsAsync(CancellationToken ct = default);
}
