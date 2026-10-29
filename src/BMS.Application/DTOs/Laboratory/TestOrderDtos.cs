using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BMS.Core.Entities;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Clinical catalog item representing an available test or package for ordering (UC32).
/// </summary>
public class TestCatalogItemDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Biochemistry";
    public string TubeType { get; set; } = "SST 4mL";
    public string TubeColor { get; set; } = "Gold";
    public double VolumeMl { get; set; } = 4.0;
    public string TurnaroundTime { get; set; } = "~2h";
    public string PreparationNotes { get; set; } = "None";
    public bool FastingRequired { get; set; }
    public int FastingHours { get; set; }
    public decimal Price { get; set; }
}

/// <summary>
/// Requisition item payload when submitting a test order.
/// </summary>
public class CreateTestOrderItemDto
{
    [Required]
    public string TestCode { get; set; } = string.Empty;

    public string? TestName { get; set; }
    public string? Category { get; set; }
    public string? TubeType { get; set; }
    public double? VolumeMl { get; set; }
    public string? TurnaroundTime { get; set; }
    public string? PreparationNotes { get; set; }

    /// <summary>Ignored. WHY kept: older clients send it; the price always comes from the server catalog (GAP C3.4).</summary>
    public decimal? Price { get; set; }
}

/// <summary>
/// Request payload for creating a new laboratory test order (UC32).
/// </summary>
public class CreateTestOrderRequestDto
{
    [Required]
    public string PatientId { get; set; } = string.Empty;

    public string? PatientName { get; set; }
    public string? PatientGender { get; set; }
    public int? PatientAge { get; set; }

    /// <summary>Ignored. WHY: the ordering doctor is always the authenticated user (JWT), never the body (GAP C3.4).</summary>
    public string? DoctorId { get; set; }
    /// <summary>Ignored; resolved from the doctor's account.</summary>
    public string? DoctorName { get; set; }

    /// <summary>ICD-10 diagnosis codes, at least one (BA:L37). Missing or malformed → 400 MSG-ERR-DOC-01.</summary>
    /// <remarks>Drafts (<see cref="SaveAsDraft"/>) may omit them; given codes must still be well-formed (Quy trình Đợt 3A Q4).</remarks>
    /// <remarks>WHY no [Required]: the business error code must be MSG-ERR-DOC-01, which model validation cannot emit.</remarks>
    public List<string>? Icd10Codes { get; set; }

    /// <summary>Single-code shortcut; merged with <see cref="Icd10Codes"/>.</summary>
    public string? Icd10Code { get; set; }

    public string Priority { get; set; } = "Routine"; // Routine, Urgent, STAT

    [Required]
    [MinLength(5, ErrorMessage = "Clinical indication or reason for test is required.")]
    public string ClinicalIndication { get; set; } = string.Empty;

    public string? SpecialInstructions { get; set; }

    public DateTime? ScheduledDate { get; set; }

    public bool SaveAsDraft { get; set; } = false;

    [Required]
    [MinLength(1, ErrorMessage = "At least one test or test package must be selected.")]
    public List<CreateTestOrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// Response payload representing a laboratory test order (UC32).
/// </summary>
public class TestOrderResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientGender { get; set; } = string.Empty;
    public int PatientAge { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Priority { get; set; } = "Routine";
    public string Status { get; set; } = "Ordered";
    public string ClinicalIndication { get; set; } = string.Empty;
    public List<string> Icd10Codes { get; set; } = new();
    /// <summary>True for STAT: sampling may start before payment (post-check).</summary>
    public bool PaymentDeferred { get; set; }
    public string? SpecialInstructions { get; set; }
    public DateTime ScheduledDate { get; set; }
    public decimal TotalAmount { get; set; }
    public bool FastingRequired { get; set; }
    public int FastingHours { get; set; }
    public List<TestOrderItemDto> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    /// <summary>Set on a cancel response when a refund request was opened (Quy trình Đợt 3A Q3).</summary>
    public string? RefundCode { get; set; }
    /// <summary>Set on a cancel response to explain the refund outcome.</summary>
    public string? RefundNote { get; set; }
}

/// <summary>
/// Test item detailed DTO inside order response.
/// </summary>
public class TestOrderItemDto
{
    public string TestCode { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string TubeType { get; set; } = string.Empty;
    public string TubeColor { get; set; } = string.Empty;
    public double VolumeMl { get; set; }
    public string TurnaroundTime { get; set; } = string.Empty;
    public string PreparationNotes { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

/// <summary>Submit of a draft order: ICD-10 codes to add to the ones already stored on the draft.</summary>
public class SubmitTestOrderRequestDto
{
    public List<string>? Icd10Codes { get; set; }
    public string? Icd10Code { get; set; }
}

/// <summary>
/// Status transition request payload.
/// </summary>
public class UpdateTestOrderStatusRequestDto
{
    [Required]
    /// <summary>Only "Cancelled" is accepted from users (QĐ10); every other transition is made by the system.</summary>
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
