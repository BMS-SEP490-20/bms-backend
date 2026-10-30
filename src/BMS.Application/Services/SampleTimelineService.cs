using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>Builds UC71 from recorded evidence; never treats RFID scans or analysis completion as result approval.</summary>
public sealed class SampleTimelineService(ISampleTimelineRepository repository) : ISampleTimelineService
{
    // WHY: matrix row UC71 — Lab, Manager, Doctor see the full chain; Admin is excluded (decision 10).
    // Ordinal comparison matches [Authorize(Roles)], so the service and the attribute agree on every role name.
    private static readonly HashSet<string> StaffRoles = new(StringComparer.Ordinal)
        { RoleNames.Manager, RoleNames.Lab, RoleNames.Doctor };
    private static readonly Dictionary<string, string> Titles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SampleCreated"] = "Đã tạo mẫu",
        ["CheckedIn"] = "Đã đăng ký tiếp đón",
        ["OrderCreated"] = "Đã tạo chỉ định",
        ["PendingCollection"] = "Chờ lấy mẫu",
        ["Collected"] = "Đã lấy mẫu máu",
        ["Collection"] = "Đã lấy mẫu máu",
        ["InTransit"] = "Đang vận chuyển",
        ["Received"] = "Mẫu đã nhập Lab",
        ["Processing"] = "Đang xử lý mẫu",
        ["Testing"] = "Đang phân tích máy",
        ["MachineAnalysis"] = "Đang phân tích máy",
        ["Completed"] = "Đã hoàn tất phân tích",
        ["UnderReview"] = "Đang thẩm định y khoa",
        ["ResultEntered"] = "Đang thẩm định y khoa",
        ["ResultAvailable"] = "Đã có kết quả chính thức",
        ["Rejected"] = "Mẫu bị từ chối",
        ["RerunRequested"] = "Yêu cầu chạy lại",
        ["Stored"] = "Đã lưu trữ mẫu",
        ["Disposed"] = "Đã hủy mẫu"
    };
    private async Task<List<string>?> Scope(SampleTimelineViewer viewer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(viewer.AccountId)) throw new ForbiddenAccessException();
        if (StaffRoles.Contains(viewer.Role)) return null;
        // Defense in depth: the controller no longer admits Patient (matrix UC71), but if a caller does,
        // scope stays limited to the account's own patient records.
        if (!viewer.Role.Equals(RoleNames.Patient, StringComparison.Ordinal)) throw new ForbiddenAccessException();
        return await repository.OwnPatientIdsAsync(viewer.AccountId, ct);
    }
    private static void Page(int page, int pageSize)
    {
        if (page < 1 || page > 10000 || pageSize < 1 || pageSize > 100)
            throw new DomainValidationException("INVALID_TIMELINE_PAGE", "Phân trang không hợp lệ.");
    }
    private static void Code(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100)
            throw new DomainValidationException("INVALID_SAMPLE_CODE", "Nhập ID mẫu hoặc barcode, tối đa 100 ký tự.");
    }
    private static SampleTimelinePage<T> Paged<T>(List<T> items, long total, int page, int size) => new(items, total, page, size, (int)Math.Ceiling((double)total / size));
    public async Task<SampleTimelinePage<SampleTimelineSummary>> ListAsync(SampleTimelineFilterDto filter, SampleTimelineViewer viewer, CancellationToken ct = default)
    {
        Page(filter.Page, filter.PageSize);
        if (filter.Search?.Length > 100 || filter.Status?.Length > 40 ||
            (!string.IsNullOrWhiteSpace(filter.Status) && !Titles.ContainsKey(filter.Status)) ||
            filter.FromDate?.Date == DateTime.MinValue.Date || filter.ToDate?.Date == DateTime.MaxValue.Date ||
            (filter.FromDate.HasValue && filter.ToDate.HasValue && filter.FromDate.Value.Date > filter.ToDate.Value.Date))
            throw new DomainValidationException("INVALID_TIMELINE_FILTER", "Bộ lọc hoặc khoảng ngày không hợp lệ.");
        var scope = await Scope(viewer, ct);
        var (items, count) = await repository.ListAsync(new(filter.Search?.Trim(), filter.Status?.Trim(), filter.FromDate, filter.ToDate, filter.Page, filter.PageSize), scope, ct);
        return Paged(scope is null ? items : items.Select(PatientSample).ToList(), count, filter.Page, filter.PageSize);
    }
    public async Task<SampleTimelineDto?> GetAsync(string code, SampleTimelinePageQuery page, SampleTimelineViewer viewer, CancellationToken ct = default)
    {
        Code(code); Page(page.Page, page.PageSize);
        var scope = await Scope(viewer, ct);
        var record = await repository.GetAsync(code.Trim(), scope, ct);
        if (record is null) return null;
        // Manual history entries cannot attest that reports passed signature/approval checks.
        var events = record.Events.Select(e => e.Code.Equals("ResultAvailable", StringComparison.OrdinalIgnoreCase) || e.Code.Equals("ResultEntered", StringComparison.OrdinalIgnoreCase)
            ? e with { Code = "Unverified:" + e.Code, Title = "Sự kiện lịch sử chưa đối chiếu báo cáo" } : e).ToList();
        var analysisComplete = record.Sample.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) || record.Sample.Status.Equals("Stored", StringComparison.OrdinalIgnoreCase);
        var ready = analysisComplete && record.Results.Count > 0 && record.Results.All(r => r.ApprovedAndSigned);
        for (var i = 0; i < record.Results.Count; i++)
            if (record.Results[i].EnteredAt is DateTime entered)
                events.Add(new("result:entered:" + i, "ResultEntered", Titles["ResultEntered"], entered, "test_results.entered_at"));
        if (ready)
        {
            // A missing approval timestamp is unknown, even when a signed approved report exists.
            var at = record.Results.All(r => r.ApprovedAt.HasValue) ? record.Results.Max(r => r.ApprovedAt) : null;
            events.Add(new("result:available", "ResultAvailable", Titles["ResultAvailable"], at, "test_results.approval"));
        }
        if (scope is not null)
        {
            // Patient responses contain only standardized progress, never free-text notes or staff/device metadata.
            events = events.Where(e => Titles.ContainsKey(e.Code) && !e.Code.Equals("Stored", StringComparison.OrdinalIgnoreCase) && !e.Code.Equals("Disposed", StringComparison.OrdinalIgnoreCase))
                .Select(e => e with
                {
                    Code = e.Code.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? "UnderReview" : e.Code,
                    OperatorName = null,
                    Location = null,
                    Notes = null,
                    Source = "Progress"
                }).ToList();
        }
        if (viewer.Role.Equals("Receptionist", StringComparison.OrdinalIgnoreCase))
        {
            // Reception staff need logistics milestones, not clinical free text embedded by another module.
            events = events.Select(e => e with { Notes = null, Title = Titles.GetValueOrDefault(e.Code, "Sự kiện đã ghi nhận") }).ToList();
        }
        events = events.Select(e => e with { Title = Titles.GetValueOrDefault(e.Code, string.IsNullOrWhiteSpace(e.Title) ? "Sự kiện đã ghi nhận" : e.Title) })
            .OrderBy(e => e.Timestamp is null).ThenBy(e => e.Timestamp).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        var steps = new[] {
            Step(1, "Đã đăng ký tiếp đón", ["CheckedIn"]), Step(2, "Đã lấy mẫu máu", ["Collected", "Collection"]),
            Step(3, "Mẫu đã nhập Lab", ["Received"]), Step(4, "Đang phân tích máy", ["Testing", "MachineAnalysis"]),
            Step(5, "Đang thẩm định y khoa", ["Completed", "UnderReview", "ResultEntered"]), Step(6, "Đã có kết quả chính thức", ["ResultAvailable"])
        }.ToList();
        return new(scope is null ? record.Sample : PatientSample(record.Sample), steps,
            Paged(events.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToList(), events.Count, page.Page, page.PageSize), ready, scope is not null);

        SampleTimelineStep Step(int number, string title, string[] codes)
        {
            var evidence = events.Where(e => codes.Contains(e.Code, StringComparer.OrdinalIgnoreCase)).ToList();
            return new(number, title, evidence.Count > 0, evidence.Where(e => e.Timestamp.HasValue).Min(e => e.Timestamp));
        }
    }
    public async Task<SampleTimelinePage<SampleTimelineRfid>?> RfidAsync(string code, SampleTimelinePageQuery page, SampleTimelineViewer viewer, CancellationToken ct = default)
    {
        Code(code); Page(page.Page, page.PageSize);
        if (await Scope(viewer, ct) is not null) throw new ForbiddenAccessException();
        var record = await repository.GetAsync(code.Trim(), null, ct);
        if (record is null) return null;
        var (items, total) = await repository.RfidAsync(record.Sample, page.Page, page.PageSize, ct);
        return Paged(items, total, page.Page, page.PageSize);
    }
    private static SampleTimelineSummary PatientSample(SampleTimelineSummary sample)
    {
        var allowed = new[] { "PendingCollection", "Collected", "InTransit", "Received", "Processing", "Testing", "Rejected", "RerunRequested" };
        var status = allowed.FirstOrDefault(s => s.Equals(sample.Status, StringComparison.OrdinalIgnoreCase)) ??
            (sample.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) || sample.Status.Equals("Stored", StringComparison.OrdinalIgnoreCase) ? "UnderReview" : "Unknown");
        return sample with { RfidTag = null, Status = status };
    }
}
