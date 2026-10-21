using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.LabProfiles;

/// <summary>Bounded directory filters; creation-date filtering excludes undated legacy profiles.</summary>
public sealed class LabProfileFilterDto
{
    [StringLength(100)] public string? Search { get; set; }
    [StringLength(100)] public string? RoomId { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    [Range(1, 10000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed record LabProfileDto(string Id, string FullName, string Qualification, string AssignedRoom,
    string RoomName, long Revision, DateTime? CreatedAt, DateTime? UpdatedAt);
public sealed record LabProfileListDto(List<LabProfileDto> Items, long Total, int Page, int PageSize, int TotalPages);
