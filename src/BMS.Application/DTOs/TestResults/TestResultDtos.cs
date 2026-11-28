using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.TestResults;

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
    /// <summary>Server-derived interpretation; Unknown never implies Normal.</summary>
    public string? Flag { get; set; }
    /// <summary>Unmodified interpretation code from a legacy parameter.</summary>
    public string? OriginalFlag { get; set; }
}

/// <summary>Test result read model for <c>TestOrder/TestResult</c>.</summary>
public class TestResultDto
{
    /// <summary>Server-derived critical flag from criterion master data.</summary>
    public bool IsPanic { get; set; }
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
    /// <summary>Clinical validation gate (DoctorApproved = 3 publishes to patient).</summary>
    public string? ValidationStatus { get; set; }
    /// <summary>Doctor account id that clinically approved the result.</summary>
    public string? ApprovedByDoctorId { get; set; }
    /// <summary>UTC time of Doctor clinical approval.</summary>
    public DateTime? ApprovedAt { get; set; }
    /// <summary>SHA-256 clinical seal required before patient portal release.</summary>
    public string? DigitalSignature { get; set; }
    /// <summary>Clinical comment / conclusion stored on the result.</summary>
    public string? ClinicalComment { get; set; }
    /// <summary>Doctor's ICD-10 diagnosis code recorded at clinical approval.</summary>
    public string? Icd10Code { get; set; }
    /// <summary>Doctor's final clinical diagnosis shown to the patient.</summary>
    public string? FinalDiagnosis { get; set; }
    /// <summary>Doctor's clinical conclusion shown to the patient.</summary>
    public string? DoctorConclusion { get; set; }
    /// <summary>Doctor's treatment advice / lifestyle recommendation.</summary>
    public string? TreatmentAdvice { get; set; }
    /// <summary>Optional follow-up appointment date.</summary>
    public string? FollowUpDate { get; set; }
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
    public string AppointmentId { get; set; } = string.Empty;
    /// <summary>Actor supplied by the controller, never accepted from request JSON.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ActorId { get; set; } = string.Empty;
    /// <summary>Required confirmation when server master-data thresholds classify a result as panic.</summary>
    public bool ConfirmRerunChecked { get; set; }
    public bool CriticalNotified { get; set; }
    public string? DoctorNotifiedName { get; set; }
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
    public bool ConfirmRerunChecked { get; set; }
    public bool CriticalNotified { get; set; }
    public string? DoctorNotifiedName { get; set; }
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
    /// <summary>Approver account id — filled from JWT in the controller (client may omit).</summary>
    public string ApproverId { get; set; } = string.Empty;
    /// <summary>Approver display name.</summary>
    public string? ApproverName { get; set; }
    /// <summary>Approver role display (defaults to Doctor).</summary>
    public string? ApproverRole { get; set; }
    /// <summary>Optional analyzer device id to record.</summary>
    public string? DeviceId { get; set; }
    /// <summary>Optional reagent quantity to record (must be &gt; 0 to apply).</summary>
    public decimal? Quantity { get; set; }
    /// <summary>Optional combined doctor conclusion note.</summary>
    public string? Notes { get; set; }
    /// <summary>WHO ICD-10 diagnosis code confirmed by the attending doctor.</summary>
    public string? Icd10Code { get; set; }
    /// <summary>Final clinical diagnosis written by the attending doctor.</summary>
    public string? FinalDiagnosis { get; set; }
    /// <summary>Doctor's detailed clinical conclusion for the patient.</summary>
    public string? DoctorConclusion { get; set; }
    /// <summary>Doctor's treatment plan / follow-up advice.</summary>
    public string? TreatmentAdvice { get; set; }
    /// <summary>Recommended follow-up date (ISO yyyy-MM-dd or display string).</summary>
    public string? FollowUpDate { get; set; }
}

/// <summary>Payload allowing an attending doctor to add or update clinical consultation fields without altering signed lab measurements.</summary>
public class UpdateClinicalConsultationDto
{
    public string? Icd10Code { get; set; }
    public string? FinalDiagnosis { get; set; }
    public string? DoctorConclusion { get; set; }
    public string? TreatmentAdvice { get; set; }
    public string? FollowUpDate { get; set; }
}
