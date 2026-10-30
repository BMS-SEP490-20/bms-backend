using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
namespace BMS.Application.Services;

/// <summary>Restricts history to clinical roles or the patient's explicit account link; audits before disclosure.</summary>
public sealed class TestHistoryService(ITestHistoryRepository repository, TimeProvider clock) : ITestHistoryService
{
    private async Task<List<string>?> Scope(TestHistoryViewer viewer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(viewer.AccountId)) throw new ForbiddenAccessException();
        // WHY: matrix row UC76 — Manager, Doctor, Lab see all; Admin and Receptionist are denied (decision 10).
        if (viewer.Role is RoleNames.Manager or RoleNames.Doctor or RoleNames.Lab) return null;
        // Patient: own records only, resolved from the account ID in the token, never from the request.
        if (viewer.Role != RoleNames.Patient) throw new ForbiddenAccessException();
        return await repository.OwnPatientsAsync(viewer.AccountId, ct);
    }
    public async Task<TestHistoryPage> ListAsync(TestHistoryFilterDto filter, TestHistoryViewer viewer, CancellationToken ct = default)
    {
        if (filter.Page < 1 || filter.Page > 10000 || filter.PageSize < 1 || filter.PageSize > 100 || filter.Search?.Length > 100 || filter.PatientId?.Length > 100 ||
            filter.FromDate?.Date == DateTime.MinValue.Date || filter.ToDate?.Date == DateTime.MaxValue.Date ||
            (filter.FromDate.HasValue && filter.ToDate.HasValue && filter.FromDate.Value.Date > filter.ToDate.Value.Date))
            throw new DomainValidationException("INVALID_TEST_HISTORY_FILTER", "Bộ lọc hoặc khoảng ngày không hợp lệ.");
        var scope = await Scope(viewer, ct);
        var (items, total) = await repository.ListAsync(new(filter.Search?.Trim(), filter.PatientId?.Trim(), filter.FromDate, filter.ToDate, filter.Page, filter.PageSize), scope, ct);
        await repository.AuditAsync(viewer, "ViewTestHistory", null, clock.GetUtcNow().UtcDateTime, ct);
        return new(items, total, filter.Page, filter.PageSize, (int)Math.Ceiling((double)total / filter.PageSize));
    }
    public async Task<TestHistoryDetail?> GetAsync(string id, TestHistoryViewer viewer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) throw new DomainValidationException("INVALID_TEST_HISTORY_ID", "ID kết quả không hợp lệ.");
        var result = await repository.GetAsync(id.Trim(), await Scope(viewer, ct), ct);
        if (result is not null) await repository.AuditAsync(viewer, "ViewTestHistoryDetail", result.Summary.Id, clock.GetUtcNow().UtcDateTime, ct);
        return result;
    }
}
