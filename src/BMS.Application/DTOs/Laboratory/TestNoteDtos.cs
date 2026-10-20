using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Request DTO for adding a test note to a specimen (UC52 / Add Test Notes).
/// </summary>
public class CreateTestNoteRequestDto
{
    [Required]
    public string SpecimenId { get; set; } = string.Empty;

    [Required]
    public string NoteType { get; set; } = "Technical observation";

    public string Visibility { get; set; } = "Internal"; // "Internal" or "Report"

    public bool IsInternalOnly { get; set; } = true;

    public bool PrintOnReport { get; set; } = false;

    public List<string>? Tags { get; set; } = new();

    [Required]
    [MinLength(1, ErrorMessage = "Note content cannot be empty.")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO representing an individual test note.
/// </summary>
public class TestNoteDto
{
    public string Id { get; set; } = string.Empty;
    public string SpecimenId { get; set; } = string.Empty;
    public string? SampleId { get; set; }
    public string SampleBarcode { get; set; } = string.Empty;
    public string? PatientId { get; set; }
    public string? PatientName { get; set; }
    public string? TestPackageName { get; set; }
    public string NoteType { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public bool IsInternalOnly { get; set; }
    public bool PrintOnReport { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Content { get; set; } = string.Empty;
    public string? CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
    public string? CreatedByUserRole { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Response DTO providing specimen context and existing notes timeline for the UI screen (UC52).
/// </summary>
public class SpecimenNoteContextDto
{
    public string SpecimenId { get; set; } = string.Empty;
    public string SampleBarcode { get; set; } = string.Empty;
    public string? PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string TestPackageName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? LabRoom { get; set; }
    public string? CollectorName { get; set; }
    public DateTime? CollectedAt { get; set; }
    public List<TestNoteDto> ExistingNotes { get; set; } = new();
}
