using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Laboratory;

/// <summary>
/// Individual verification check item within the technical verification checklist (UC72).
/// </summary>
public class VerificationCheckItemDto
{
    public string CheckName { get; set; } = string.Empty;
    public string Status { get; set; } = "OK"; // "OK", "Flag", "None", "Confirmed"
    public string StatusType { get; set; } = "ok"; // "ok", "warn", "err"
    public string Details { get; set; } = string.Empty;
}

/// <summary>
/// Full verification checklist and specimen diagnostic packet DTO (UC72 / Issue I3-073).
/// </summary>
public class TestResultVerificationDetailDto
{
    public string SpecimenId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string PackageName { get; set; } = "Lipid Profile";
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public List<VerificationCheckItemDto> Checklist { get; set; } = new();
    public string VerifierComment { get; set; } = string.Empty;
    public string Status { get; set; } = "PendingVerification";
    public string VerifiedBy { get; set; } = string.Empty;
    public DateTime? VerifiedAt { get; set; }
    public string? ReturnedReason { get; set; }
    public int FlagCount { get; set; }
}

/// <summary>
/// Payload submitted when technical verifier approves results and advances them to doctor approval.
/// </summary>
public class VerifyTestResultRequestDto
{
    [Required]
    [MinLength(10, ErrorMessage = "Verifier comment must contain at least 10 characters.")]
    public string VerifierComment { get; set; } = string.Empty;
}

/// <summary>
/// Payload submitted when technical verifier returns results to technician for correction.
/// </summary>
public class ReturnTestResultRequestDto
{
    [Required]
    [MinLength(10, ErrorMessage = "Return reason must contain at least 10 characters.")]
    public string ReturnReason { get; set; } = string.Empty;
}

/// <summary>
/// Item in the verifier queue awaiting technical verification.
/// </summary>
public class VerificationQueueItemDto
{
    public string SpecimenId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public int FlagCount { get; set; }
    public string Priority { get; set; } = "Normal";
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "PendingVerification";
}
