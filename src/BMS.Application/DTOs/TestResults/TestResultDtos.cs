using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.TestResults;

// These DTOs were referenced by Approve Test Results (I3-004) but never committed; they are
// reconstructed from the service/controller usage and the web client payloads (labTestService.js).

/// <summary>One measured parameter of a test result (stored as a <c>TestResultItem</c>).</summary>
public class TestParameterDto
{
    /// <summary>Test criterion id.</summary>
    public string CriteriaId { get; set; } = string.Empty;
    /// <summary>Parameter display name.</summary>
    public string ParamName { get; set; } = string.Empty;
    /// <summary>Abbreviation / criterion code, e.g. WBC.</summary>
    public string? Abbreviation { get; set; }
    /// <summary>Measured value as entered.</summary>
    public string? Value { get; set; }
}

/// <summary>Test result read model for <c>TestOrder/TestResult</c>.</summary>
public class TestResultDto
{
    /// <summary>Result identifier.</summary>
    public string TestResultId { get; set; } = string.Empty;
    /// <summary>Appointment / test order id.</summary>
    public string AppointmentId { get; set; } = string.Empty;
    /// <summary>Sample id.</summary>
    public string SampleId { get; set; } = string.Empty;
    /// <summary>Reagent code used.</summary>
    public string? ReagentCode { get; set; }
    /// <summary>Reagent quantity consumed.</summary>
    public int Quantity { get; set; }
    /// <summary>Analyzer device id.</summary>
    public string? DeviceId { get; set; }
    /// <summary>Reviewer display.</summary>
    public string? ReviewedBy { get; set; }
    /// <summary>Review time (UTC).</summary>
    public DateTime? ReviewedAt { get; set; }
    /// <summary>Workflow status, e.g. waiting_review, Completed.</summary>
    public string? Status { get; set; }
    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Last update time (UTC).</summary>
    public DateTime? UpdatedAt { get; set; }
    /// <summary>Measured parameters.</summary>
    public List<TestParameterDto> Parameters { get; set; } = [];
}

/// <summary>Payload creating a result together with its parameters.</summary>
public class CreateFullTestResultDto
{
    /// <summary>Appointment / test order id.</summary>
    [Required]
    public string AppointmentId { get; set; } = string.Empty;
    /// <summary>Sample id.</summary>
    [Required]
    public string SampleId { get; set; } = string.Empty;
    /// <summary>Reagent code used.</summary>
    public string? ReagentCode { get; set; }
    /// <summary>Reagent quantity consumed.</summary>
    [Range(0, 1_000_000)]
    public decimal Quantity { get; set; }
    /// <summary>Analyzer device id.</summary>
    public string? DeviceId { get; set; }
    /// <summary>Reviewer display.</summary>
    public string? ReviewedBy { get; set; }
    /// <summary>Initial workflow status.</summary>
    public string? Status { get; set; }
    /// <summary>Measured parameters.</summary>
    public List<TestParameterDto> Parameters { get; set; } = [];
}

/// <summary>Partial update; null fields (and Quantity 0) are left unchanged.</summary>
public class UpdateTestResultDto
{
    /// <summary>Reagent code used.</summary>
    public string? ReagentCode { get; set; }
    /// <summary>Reagent quantity; 0 keeps the current value.</summary>
    [Range(0, 1_000_000)]
    public decimal Quantity { get; set; }
    /// <summary>Analyzer device id.</summary>
    public string? DeviceId { get; set; }
    /// <summary>Reviewer display.</summary>
    public string? ReviewedBy { get; set; }
    /// <summary>New workflow status.</summary>
    public string? Status { get; set; }
    /// <summary>Review time; defaults to now.</summary>
    public DateTime? ReviewedAt { get; set; }
    /// <summary>When provided, replaces all parameters.</summary>
    public List<TestParameterDto>? Parameters { get; set; }
}

/// <summary>Approval metadata (I3-004).</summary>
public class ApproveTestResultDto
{
    /// <summary>Approver account id.</summary>
    [Required]
    public string ApproverId { get; set; } = string.Empty;
    /// <summary>Approver display name.</summary>
    public string? ApproverName { get; set; }
    /// <summary>Approver role display (defaults to Doctor).</summary>
    public string? ApproverRole { get; set; }
    /// <summary>Optional analyzer device id to record.</summary>
    public string? DeviceId { get; set; }
    /// <summary>Optional reagent quantity to record (must be &gt; 0 to apply).</summary>
    public decimal? Quantity { get; set; }
}
