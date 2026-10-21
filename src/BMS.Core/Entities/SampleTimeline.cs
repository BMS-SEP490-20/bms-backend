namespace BMS.Core.Entities;

/// <summary>Read-only evidence from existing sample documents; absent dates remain absent.</summary>
public sealed record SampleTimelineSummary(string Id, string Barcode, string PatientId, string Status, string? RfidTag, DateTime? CollectedAt);
public sealed record SampleTimelineEvent(string Id, string Code, string Title, DateTime? Timestamp, string Source,
    string? OperatorName = null, string? Location = null, string? Notes = null);
public sealed record SampleTimelineResult(bool ApprovedAndSigned, DateTime? EnteredAt, DateTime? ApprovedAt);
public sealed record SampleTimelineRecord(SampleTimelineSummary Sample, List<SampleTimelineEvent> Events, List<SampleTimelineResult> Results);
public sealed record SampleTimelineRfid(string Id, DateTime? Timestamp, string? Checkpoint, string? Reader, string? OperatorName, string LinkType, string? Status);
public sealed record SampleTimelineSearch(string? Search, string? Status, DateTime? FromDate, DateTime? ToDate, int Page, int PageSize);
public sealed record SampleTimelineViewer(string AccountId, string Role);
