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

    public string? DoctorId { get; set; }
    public string? DoctorName { get; set; }

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
    public string? SpecialInstructions { get; set; }
    public DateTime ScheduledDate { get; set; }
    public decimal TotalAmount { get; set; }
    public bool FastingRequired { get; set; }
    public int FastingHours { get; set; }
    public List<TestOrderItemDto> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
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
    public double VolumeMl { get; set; }
    public string TurnaroundTime { get; set; } = string.Empty;
    public string PreparationNotes { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

/// <summary>
/// Status transition request payload.
/// </summary>
public class UpdateTestOrderStatusRequestDto
{
    [Required]
    public string Status { get; set; } = string.Empty; // Ordered, InAnalysis, Completed, Cancelled
    public string? Notes { get; set; }
}
