namespace BMS.Application.DTOs.Billing;

/// <summary>Refund as shown to Manager / Receptionist.</summary>
public sealed class RefundDto
{
    public string Id { get; set; } = string.Empty;
    public string RefundCode { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? InvoiceId { get; set; }
    public string? PaymentMethod { get; set; }
    /// <summary>Counter (cash/POS, paid out by the Receptionist) or VnPay.</summary>
    public string? Channel { get; set; }
    /// <summary>Returned to the patient = what the patient paid.</summary>
    public decimal Amount { get; set; }
    /// <summary>BHYT part: reversal entry only, never paid to the patient.</summary>
    public decimal BhytReversalAmount { get; set; }
    public DateTime? BhytReversalPostedAt { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public string? PaidOutBy { get; set; }
    public DateTime? PaidOutAt { get; set; }
}

/// <summary>Manager exception refund for an already cancelled, paid order.</summary>
public sealed class OpenRefundRequestDto
{
    public string OrderId { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>Reason of a Manager decision (required for reject).</summary>
public sealed class RefundDecisionRequestDto
{
    public string? Note { get; set; }
}
