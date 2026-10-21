using System.ComponentModel.DataAnnotations;
using BMS.Core.Entities;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>Bounded sample search; dates refer to inclusive clinic collection days (UTC+7).</summary>
public sealed class SampleTimelineFilterDto
{
    [StringLength(100)] public string? Search { get; set; }
    [StringLength(40)] public string? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    [Range(1, 10000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}
public sealed class SampleTimelinePageQuery
{
    [Range(1, 10000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 50;
}
public sealed record SampleTimelinePage<T>(List<T> Items, long Total, int Page, int PageSize, int TotalPages);
public sealed record SampleTimelineStep(int Number, string Title, bool Recorded, DateTime? Timestamp);
public sealed record SampleTimelineDto(SampleTimelineSummary Sample, List<SampleTimelineStep> Steps,
    SampleTimelinePage<SampleTimelineEvent> Events, bool ResultsAvailable, bool PatientView);
