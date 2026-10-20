using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace BMS.Application.DTOs.TestBatches;

public class WellPlatePositionInputDto
{
    [Required]
    public string WellCoordinate { get; set; } = string.Empty; // e.g. "A1", "A2", "B1"

    public bool IsControlSample { get; set; } = false;
    public string? ControlLevel { get; set; } // "IQC Level 1", "IQC Level 2"
    public string? SampleId { get; set; }
    public string? Barcode { get; set; }
    public string? PatientName { get; set; }
    public string? Priority { get; set; }
}

public class CreateTestBatchRequest : IValidatableObject
{
    [Required(ErrorMessage = "Mã máy phân tích (AnalyzerId) là bắt buộc.")]
    public string AnalyzerId { get; set; } = string.Empty;

    public string AnalyzerName { get; set; } = string.Empty;

    public string Category { get; set; } = "Biochemistry";

    [Range(12, 192, ErrorMessage = "Sức chứa khay máy phải từ 12 đến 192 vị trí.")]
    public int Capacity { get; set; } = 48;

    public List<string> SampleIds { get; set; } = new();

    public List<WellPlatePositionInputDto> Wells { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 1. Capacity validation
        var totalOccupied = Wells.Count;
        if (totalOccupied > Capacity)
        {
            yield return new ValidationResult(
                $"Số lượng mẫu ({totalOccupied}) vượt quá sức chứa tối đa của khay máy ({Capacity} vị trí).",
                new[] { nameof(Wells), nameof(Capacity) });
        }

        // 2. Duplicate well collision check
        var duplicateCoordinates = Wells
            .GroupBy(w => w.WellCoordinate.Trim().ToUpperInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateCoordinates.Any())
        {
            yield return new ValidationResult(
                $"Tọa độ giếng khay bị trùng lặp: {string.Join(", ", duplicateCoordinates)}",
                new[] { nameof(Wells) });
        }

        // 3. ISO 15189 IQC Control Rule: Wells A1 and A2 must be reserved for Controls
        var a1 = Wells.FirstOrDefault(w => string.Equals(w.WellCoordinate, "A1", StringComparison.OrdinalIgnoreCase));
        var a2 = Wells.FirstOrDefault(w => string.Equals(w.WellCoordinate, "A2", StringComparison.OrdinalIgnoreCase));

        if (a1 != null && !a1.IsControlSample)
        {
            yield return new ValidationResult(
                "Tiêu chuẩn ISO 15189: Giếng A1 bắt buộc dành cho mẫu kiểm chuẩn IQC Level 1.",
                new[] { nameof(Wells) });
        }

        if (a2 != null && !a2.IsControlSample)
        {
            yield return new ValidationResult(
                "Tiêu chuẩn ISO 15189: Giếng A2 bắt buộc dành cho mẫu kiểm chuẩn IQC Level 2.",
                new[] { nameof(Wells) });
        }
    }
}

public class UpdateTestBatchStatusRequest
{
    [Required(ErrorMessage = "Trạng thái mới là bắt buộc (Running, Completed, Cancelled).")]
    public string Status { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? CancelReason { get; set; }
}

public class TestBatchDto
{
    public string Id { get; set; } = string.Empty;
    public string BatchCode { get; set; } = string.Empty;
    public string AnalyzerId { get; set; } = string.Empty;
    public string AnalyzerName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int SampleCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<WellPlatePositionDto> Wells { get; set; } = new();
    public string TechnicianId { get; set; } = string.Empty;
    public string? TechnicianName { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class WellPlatePositionDto
{
    public string WellCoordinate { get; set; } = string.Empty;
    public bool IsControlSample { get; set; }
    public string? ControlLevel { get; set; }
    public string? SampleId { get; set; }
    public string? Barcode { get; set; }
    public string? PatientName { get; set; }
    public string? Priority { get; set; }
}
