using BMS.Core.Entities;
namespace BMS.Core.Interfaces;

/// <summary>Reads existing result collections and records access in the existing audit log.</summary>
public interface ITestHistoryRepository
{
    Task<List<string>> OwnPatientsAsync(string accountId, CancellationToken ct = default);
    Task<(List<TestHistorySummary> Items, long Total)> ListAsync(TestHistorySearch search, List<string>? scope, CancellationToken ct = default);
    Task<TestHistoryDetail?> GetAsync(string id, List<string>? scope, CancellationToken ct = default);
    Task AuditAsync(TestHistoryViewer viewer, string action, string? target, DateTime at, CancellationToken ct = default);
}
