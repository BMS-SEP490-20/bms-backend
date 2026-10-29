using BMS.Application.DTOs.Billing;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

/// <summary>
/// Refunds of cancelled paid orders (Quy trình Đợt 3A Q3, provisional until Phát confirms UC55):
/// cancel before collection → refund request of exactly what the patient paid, same channel, Manager approval,
/// BHYT part only reversed; sample collected → nothing automatic (Manager exception). Every step is audited.
/// </summary>
public interface IOrderRefundService
{
    /// <summary>
    /// Called after an order was cancelled. Voids pending (Unpaid) invoices; if the order was paid and no specimen was
    /// collected, opens a PendingApproval refund. Returns the refund, or null when nothing is refunded automatically.
    /// </summary>
    Task<Refund?> OnOrderCancelledAsync(TestOrder cancelled, bool sampleCollected, Actor actor, string? reason, CancellationToken ct = default);

    /// <summary>Manager exception: opens a refund for a cancelled paid order (e.g. after collection). Reason required.</summary>
    Task<RefundDto> OpenManagerExceptionAsync(OpenRefundRequestDto request, Actor manager, CancellationToken ct = default);

    /// <summary>Manager approval: fixes amount/channel from the Paid invoice and posts the BHYT reversal entry.</summary>
    Task<RefundDto> ApproveAsync(string refundId, RefundDecisionRequestDto request, Actor manager, CancellationToken ct = default);

    /// <summary>Manager rejection (note required).</summary>
    Task<RefundDto> RejectAsync(string refundId, RefundDecisionRequestDto request, Actor manager, CancellationToken ct = default);

    /// <summary>Receptionist pays an approved counter refund back (cash/POS) → Completed, invoice Refunded.</summary>
    Task<RefundDto> PayOutAtCounterAsync(string refundId, Actor receptionist, CancellationToken ct = default);

    /// <summary>Refund by id or code.</summary>
    Task<RefundDto> GetAsync(string refundId, CancellationToken ct = default);

    /// <summary>Refunds, newest first, optionally by status name.</summary>
    Task<IReadOnlyList<RefundDto>> ListAsync(string? status, CancellationToken ct = default);
}
