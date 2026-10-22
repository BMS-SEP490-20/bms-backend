namespace BMS.Core.Entities;

/// <summary>UC76 stored result metadata; missing timestamps and ranges are never inferred.</summary>
public sealed record TestHistorySummary(string Id, string ResultCode, string PatientId, string SampleId, string PackageName,
    DateTime? CreatedAt, DateTime? ApprovedAt, bool Available);
public sealed record TestHistoryItem(string Code, string Name, string Value, string Unit, string ReferenceRange, string Flag);
public sealed record TestHistoryDetail(TestHistorySummary Summary, List<TestHistoryItem> Items, string? Conclusion);
public sealed record TestHistorySearch(string? Search, string? PatientId, DateTime? FromDate, DateTime? ToDate, int Page, int PageSize);
public sealed record TestHistoryViewer(string AccountId, string Role, string? IpAddress = null);
