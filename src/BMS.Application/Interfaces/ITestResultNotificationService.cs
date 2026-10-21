using BMS.Application.DTOs.Notifications;
using BMS.Application.DTOs.Roles;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for dispatching, tracking, and delivering test result notifications (UC60 / BR-182).
/// Enforces critical panic value escalation, multi-channel dispatch, and read receipts.
/// </summary>
public interface ITestResultNotificationService
{
    Task<(bool Success, string Message, TestResultNotificationDetailDto? Data)> DispatchNotificationAsync(
        DispatchTestResultNotificationDto dto,
        string operatorId,
        string operatorName,
        CancellationToken ct = default);

    Task<TestResultNotificationDetailDto?> GetNotificationByIdAsync(string id, CancellationToken ct = default);

    Task<PagedResult<TestResultNotificationListItemDto>> GetNotificationsPagedAsync(
        NotificationQueryParameters query,
        CancellationToken ct = default);

    Task<List<TestResultNotificationDetailDto>> GetPatientNotificationsAsync(
        string patientId,
        CancellationToken ct = default);

    Task<bool> MarkAsReadAsync(string id, CancellationToken ct = default);

    Task<(bool Success, string Message, TestResultNotificationDetailDto? Data)> ResendNotificationAsync(
        string id,
        ResendNotificationDto dto,
        string operatorName,
        CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(string patientId, CancellationToken ct = default);

    Task<NotificationStatsDto> GetStatsAsync(CancellationToken ct = default);
}
