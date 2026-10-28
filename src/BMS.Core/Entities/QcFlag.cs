using System;
using System.ComponentModel.DataAnnotations;

namespace Testorder_service.Models.Entities
{
    /// <summary>
    /// Represents a Quality Control (QC) flag triggered by laboratory analyzers or out-of-bound threshold rules.
    /// Handled by Laboratory Users and Clinic Managers.
    /// </summary>
    public class QcFlag
    {
        [Key]
        public string FlagId { get; set; } = null!;

        public string FlagCode { get; set; } = null!; // e.g. QC-CAL-001, QC-WESTGARD-13S

        public string AnalyzerId { get; set; } = null!;

        public string AnalyzerName { get; set; } = null!;

        public string Parameter { get; set; } = null!; // RBC, WBC, Hemoglobin, Glucose, etc.

        public string RuleType { get; set; } = "Threshold_Out_Of_Bounds"; // Westgard_1_3s, Westgard_2_2s, Calibration_Drift, etc.

        public string Severity { get; set; } = "HIGH"; // CRITICAL, HIGH, MEDIUM, LOW

        public string Status { get; set; } = "ACTIVE"; // ACTIVE, INVESTIGATING, RESOLVED, OVERRIDDEN, DISMISSED

        public decimal ObservedValue { get; set; }

        public decimal TargetValue { get; set; }

        public decimal LowerLimit { get; set; }

        public decimal UpperLimit { get; set; }

        public string Unit { get; set; } = "";

        public string? SampleBarcode { get; set; }

        public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

        public string TriggeredBy { get; set; } = "QC Analyzer Engine";

        public DateTime? ResolvedAt { get; set; }

        public string? ResolvedBy { get; set; }

        public string? ResolutionNotes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
