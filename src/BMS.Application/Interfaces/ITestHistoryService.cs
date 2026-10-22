using BMS.Application.DTOs.Laboratory;
using BMS.Core.Entities;
namespace BMS.Application.Interfaces;

/// <summary>UC76 server-scoped and audited history access.</summary>
public interface ITestHistoryService
{
    Task<TestHistoryPage> ListAsync(TestHistoryFilterDto filter, TestHistoryViewer viewer, CancellationToken ct = default);
    Task<TestHistoryDetail?> GetAsync(string id, TestHistoryViewer viewer, CancellationToken ct = default);
}
