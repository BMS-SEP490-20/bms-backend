using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Checked-in appointment ready for phlebotomy intake (UC37).
/// </summary>
public class CheckedInAppointmentDto
{
    public string AppointmentId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public List<string> Tubes { get; set; } = new();
    public string Priority { get; set; } = "Routine";
}

/// <summary>
/// Request payload to confirm and create a sample collection batch list (UC37).
/// </summary>
public class CreateSampleListRequestDto
{
    [Required]
    [MinLength(1, ErrorMessage = "At least one appointment must be selected for the collection run.")]
    public List<string> AppointmentIds { get; set; } = new();

    public string Location { get; set; } = "Lab Room 02 — Phlebotomy";

    public string TargetTime { get; set; } = "08:30";

    public string? CollectorId { get; set; }

    public string? CollectorName { get; set; }
}

/// <summary>
/// Detailed response payload for sample collection list batch.
/// </summary>
public class SampleListResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string BatchCode { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string TargetTime { get; set; } = string.Empty;
    public DateTime ScheduledDate { get; set; }
    public string CollectorName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalSpecimens { get; set; }
    public List<SampleCollectionItemDto> Items { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Specimen item within collection list response.
/// </summary>
public class SampleCollectionItemDto
{
    public int Index { get; set; }
    public string SpecimenId { get; set; } = string.Empty;
    public string AppointmentId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public List<string> Tubes { get; set; } = new();
    public string CollectorName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Printable specimen tube barcode label data (Code 128 / BR-07).
/// </summary>
public class SpecimenBarcodeLabelDto
{
    public string SpecimenId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Tube { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string BarcodeCode128 { get; set; } = string.Empty;
    public string SvgBarcode { get; set; } = string.Empty;
}

public class BatchPrintBarcodesResponseDto
{
    public string BatchCode { get; set; } = string.Empty;
    public int LabelCount { get; set; }
    public List<SpecimenBarcodeLabelDto> Labels { get; set; } = new();
}
