using System.ComponentModel.DataAnnotations;
using BMS.Core.Entities;
namespace BMS.Application.DTOs.Laboratory;

/// <summary>Filters by stored result creation date using inclusive UTC+7 clinic days.</summary>
public sealed class TestHistoryFilterDto
{
    [StringLength(100)] public string? Search { get; set; }
    [StringLength(100)] public string? PatientId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    [Range(1, 10000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}
public sealed record TestHistoryPage(List<TestHistorySummary> Items, long Total, int Page, int PageSize, int TotalPages);
