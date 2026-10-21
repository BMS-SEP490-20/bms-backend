using System.ComponentModel.DataAnnotations;
using BMS.Application.DTOs.MedicalRecords;

namespace BMS.Application.DTOs.Clinic;

/// <summary>UC51 input. Patient, author and timestamp are resolved by the server.</summary>
public sealed class CreateMedicalNoteDto
{
    [Required, StringLength(100)]
    public string RecordId { get; set; } = string.Empty;
    [Required, StringLength(5000)]
    public string Content { get; set; } = string.Empty;
    /// <summary>A client-generated UUID retained when retrying the same submission.</summary>
    [Required]
    public Guid SubmissionId { get; set; }
}

/// <summary>Read-only medical note returned to the treating doctor.</summary>
public sealed record MedicalNoteDto(string Id, string RecordId, string Content,
    string AuthorName, DateTime CreatedAt);

/// <summary>Existing EMR context and a bounded page of supplementary notes.</summary>
public sealed record MedicalNoteContextDto(MedicalRecordDto Record,
    List<MedicalNoteDto> Items, long Total, int Page, int PageSize);
