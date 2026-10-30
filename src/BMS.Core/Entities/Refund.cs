using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>Lifecycle of a refund (Quy trình ruling Đợt 3A Q3; UC55 still to be confirmed by Phát).</summary>
/// <remarks>Numeric values are stored; append new values, never renumber.</remarks>
public enum RefundStatus
{
    /// <summary>Opened by a cancel (or by the Manager as an exception); waits for Manager approval.</summary>
    PendingApproval = 1,
    /// <summary>Approved, cash/POS: the Receptionist pays it back at the counter.</summary>
    ApprovedAwaitingPayout = 2,
    /// <summary>Approved, VNPay: to be returned through VNPay (gateway refund call = Đợt 4, with the IPN).</summary>
    ApprovedAwaitingGateway = 3,
    /// <summary>Money returned to the patient.</summary>
    Completed = 4,
    /// <summary>Refused by the Manager (reason recorded).</summary>
    Rejected = 5
}

/// <summary>Channel the money goes back through: always the channel the patient paid with.</summary>
public enum RefundChannel
{
    /// <summary>Cash or POS card at the counter, paid out by the Receptionist.</summary>
    Counter = 1,
    /// <summary>Back through VNPay.</summary>
    VnPay = 2
}

/// <summary>How the refund was opened.</summary>
public enum RefundKind
{
    /// <summary>Automatically when a paid order is cancelled before sample collection.</summary>
    CancelBeforeCollection = 1,
    /// <summary>Opened by the Manager as an exception (e.g. cancelled after collection, where nothing is automatic).</summary>
    ManagerException = 2
}

/// <summary>
/// Refund of a cancelled, paid test order (collection <c>refunds</c>, one per order).
/// </summary>
/// <remarks>
/// <see cref="Amount"/> = exactly what the patient paid (the Paid invoice total, fixed at approval). The BHYT part is
/// never paid to the patient: it is only recorded as a reversal entry (<see cref="BhytReversalAmount"/>).
/// </remarks>
[BsonIgnoreExtraElements]
public class Refund
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>RF-yyyyMMdd-XXXX from the atomic counter.</summary>
    [BsonElement("refund_code")]
    public string RefundCode { get; set; } = string.Empty;

    [BsonElement("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("order_number")]
    public string OrderNumber { get; set; } = string.Empty;

    [BsonElement("patient_id")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("kind")]
    public RefundKind Kind { get; set; }

    [BsonElement("status")]
    public RefundStatus Status { get; set; } = RefundStatus.PendingApproval;

    [BsonElement("reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Paid invoice being refunded (resolved again at approval; may be null right after a cancel race).</summary>
    [BsonElement("invoice_id")]
    [BsonIgnoreIfNull]
    public string? InvoiceId { get; set; }

    /// <summary>Original payment method of the invoice.</summary>
    [BsonElement("payment_method")]
    [BsonIgnoreIfNull]
    public PaymentMethod? PaymentMethod { get; set; }

    [BsonElement("channel")]
    [BsonIgnoreIfNull]
    public RefundChannel? Channel { get; set; }

    /// <summary>Amount returned to the patient (= invoice total the patient paid).</summary>
    [BsonElement("amount")]
    public decimal Amount { get; set; }

    /// <summary>BHYT deduction of the invoice, reversed in the books only (never paid to the patient).</summary>
    [BsonElement("bhyt_reversal_amount")]
    public decimal BhytReversalAmount { get; set; }

    [BsonElement("bhyt_reversal_posted_at")]
    [BsonIgnoreIfNull]
    public DateTime? BhytReversalPostedAt { get; set; }

    [BsonElement("requested_by")]
    public string RequestedBy { get; set; } = string.Empty;

    [BsonElement("requested_at")]
    public DateTime RequestedAt { get; set; }

    [BsonElement("decided_by")]
    [BsonIgnoreIfNull]
    public string? DecidedBy { get; set; }

    [BsonElement("decided_at")]
    [BsonIgnoreIfNull]
    public DateTime? DecidedAt { get; set; }

    [BsonElement("decision_note")]
    [BsonIgnoreIfNull]
    public string? DecisionNote { get; set; }

    [BsonElement("paid_out_by")]
    [BsonIgnoreIfNull]
    public string? PaidOutBy { get; set; }

    [BsonElement("paid_out_at")]
    [BsonIgnoreIfNull]
    public DateTime? PaidOutAt { get; set; }
}
