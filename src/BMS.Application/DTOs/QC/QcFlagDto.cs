using System;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.QC
{
    /// <summary>
    /// Data transfer object for creating a new Quality Control flag.
    /// </summary>
    public class CreateQcFlagDto
    {
        public string? FlagCode { get; set; }

        [Required]
        public string AnalyzerId { get; set; } = null!;

        [Required]
        public string AnalyzerName { get; set; } = null!;

        [Required]
        public string Parameter { get; set; } = null!;

        public string RuleType { get; set; } = "Threshold_Out_Of_Bounds";
        public string Severity { get; set; } = "HIGH"; // CRITICAL, HIGH, MEDIUM, LOW
        public string Status { get; set; } = "ACTIVE";
        public decimal ObservedValue { get; set; }
        public decimal TargetValue { get; set; }
        public decimal LowerLimit { get; set; }
        public decimal UpperLimit { get; set; }
        public string Unit { get; set; } = "";
        public string? SampleBarcode { get; set; }
        public string? TriggeredBy { get; set; }
        public string? ResolutionNotes { get; set; }
    }

    /// <summary>
    /// Data transfer object for updating status, severity, or resolution details of an existing QC flag.
    /// </summary>
    public class UpdateQcFlagDto
    {
        public string? Status { get; set; } // ACTIVE, INVESTIGATING, RESOLVED, OVERRIDDEN, DISMISSED
        public string? Severity { get; set; }
        public string? ResolutionNotes { get; set; }
        public string? ResolvedBy { get; set; }
        public decimal? ObservedValue { get; set; }
        public decimal? TargetValue { get; set; }
        public decimal? LowerLimit { get; set; }
        public decimal? UpperLimit { get; set; }
    }

    /// <summary>QC flag counters computed by the database.</summary>
    public sealed record QcFlagStatisticsDto(long TotalFlags, long ActiveFlags, long InvestigatingFlags, long ResolvedFlags, long CriticalFlags);
}
