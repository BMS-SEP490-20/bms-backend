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

/// <summary>Professional details only; creating a profile never creates a login account.</summary>
public sealed class CreateLabProfileDto
{
    [Required, StringLength(100)] public string AccountId { get; set; } = "";
    [Required, StringLength(200)] public string FullName { get; set; } = "";
    [Required, StringLength(500)] public string Qualification { get; set; } = "";
    [Required, StringLength(100)] public string AssignedRoom { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = "";
}
public sealed record LabProfileAccountsDto(List<BMS.Core.Entities.LabProfileAccount> Items, long Total, int Page, int PageSize, int TotalPages);

/// <summary>Account identity is immutable; callers must submit the version they viewed.</summary>
public sealed class UpdateLabProfileDto
{
    [Required, StringLength(200)] public string FullName { get; set; } = "";
    [Required, StringLength(500)] public string Qualification { get; set; } = "";
    [Required, StringLength(100)] public string AssignedRoom { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = "";
    [Required, Range(typeof(long), "0", "9223372036854775806")] public long? ExpectedRevision { get; set; }
}
