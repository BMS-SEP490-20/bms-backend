using BMS.Application.DTOs.AuditLogs;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <inheritdoc />
/// <remarks>
/// Quy trình ruling Đợt 3A Q3 (provisional, waits for Phát on UC55):
/// <list type="bullet">
/// <item>Paid order cancelled before collection → refund exactly what the patient paid (the Paid invoice total),
/// through the channel used: VNPay → VNPay, cash/POS → the Receptionist at the counter.</item>
/// <item>BHYT part: reversal entry only (<see cref="Refund.BhytReversalAmount"/>), never paid to the patient.</item>
/// <item>Manager approval required; every step audited.</item>
/// <item>Sample already collected → no automatic refund; the Manager may open one as an exception.</item>
/// </list>
/// </remarks>
public sealed class OrderRefundService(
    IRefundRepository refunds,
    IInvoiceRepository invoices,
    ITestOrderRepository orders,
    ISequenceGenerator sequences,
    IAuditLogService audit,
    TimeProvider clock,
    ILogger<OrderRefundService> logger) : IOrderRefundService
{
    private const int MinReasonLength = 5;

    /// <inheritdoc />
    public async Task<Refund?> OnOrderCancelledAsync(TestOrder cancelled, bool sampleCollected, Actor actor, string? reason,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cancelled);
        // A pending VNPay invoice must never be settled for a cancelled order.
        var voided = await invoices.VoidUnpaidByOrderIdAsync(cancelled.Id, ct);
        if (voided > 0)
            await AuditAsync(actor, "INVOICE_VOIDED_ON_CANCEL", "Invoice", cancelled.OrderNumber, $"{voided} unpaid invoice(s) voided", "INFO", ct);
        if (cancelled.PaidAt == null) return null;
        if (sampleCollected)
        {
            await AuditAsync(actor, "REFUND_NOT_AUTOMATIC", "TestOrder", cancelled.OrderNumber,
                "Paid order cancelled after sample collection: no automatic refund (Manager exception only)", "WARNING", ct);
            return null;
        }
        var refund = await NewRefundAsync(cancelled, RefundKind.CancelBeforeCollection, actor,
            string.IsNullOrWhiteSpace(reason) ? "Hủy đơn trước khi lấy mẫu" : reason.Trim(), ct);
        if (!await refunds.TryInsertAsync(refund, ct))
            return await refunds.GetByOrderIdAsync(cancelled.Id, ct); // already opened (idempotent)
        await AuditAsync(actor, "REFUND_REQUESTED", "Refund", refund.RefundCode,
            $"order {cancelled.OrderNumber}; expected {refund.Amount} via {refund.Channel?.ToString() ?? "?"}; awaiting Manager approval", "WARNING", ct);
        logger.LogInformation("Refund {RefundCode} opened for cancelled order {OrderNumber}", refund.RefundCode, cancelled.OrderNumber);
        return refund;
    }

    /// <inheritdoc />
    public async Task<RefundDto> OpenManagerExceptionAsync(OpenRefundRequestDto request, Actor manager, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = RequireReason(request.Reason);
        var order = (string.IsNullOrWhiteSpace(request.OrderId) ? null
                        : await orders.GetByIdAsync(request.OrderId.Trim(), ct) ?? await orders.GetByOrderNumberAsync(request.OrderId.Trim(), ct))
                    ?? throw new ResourceNotFoundException("ORDER_NOT_FOUND", "Không tìm thấy đơn xét nghiệm.");
        if (order.Status != TestOrderStatus.Cancelled)
            throw new ConflictStateException("REFUND_ORDER_NOT_CANCELLED", "Chỉ hoàn tiền cho đơn đã hủy.");
        if (order.PaidAt == null)
            throw new ConflictStateException("REFUND_ORDER_NOT_PAID", "Đơn chưa thanh toán, không có gì để hoàn.");
        var refund = await NewRefundAsync(order, RefundKind.ManagerException, manager, reason, ct);
        if (!await refunds.TryInsertAsync(refund, ct))
            throw new ConflictStateException("REFUND_ALREADY_EXISTS", "Đơn này đã có yêu cầu hoàn tiền.");
        await AuditAsync(manager, "REFUND_REQUESTED", "Refund", refund.RefundCode,
            $"Manager exception for order {order.OrderNumber}; reason: {reason}", "WARNING", ct);
        return Map(refund);
    }

    /// <inheritdoc />
    public async Task<RefundDto> ApproveAsync(string refundId, RefundDecisionRequestDto request, Actor manager, CancellationToken ct = default)
    {
        var refund = await LoadAsync(refundId, ct);
        if (refund.Status != RefundStatus.PendingApproval) throw NotPending(refund);
        // WHY resolve again: a cancel can race the counter payment (order marked paid, invoice written a moment later).
        var invoice = await invoices.GetPaidByOrderIdAsync(refund.OrderId, ct)
                      ?? throw new ConflictStateException("REFUND_INVOICE_NOT_FOUND", "Không tìm thấy hóa đơn đã thanh toán của đơn này.");
        var channel = ChannelOf(invoice.PaymentMethod);
        var now = clock.GetUtcNow().UtcDateTime;
        var next = channel == RefundChannel.VnPay ? RefundStatus.ApprovedAwaitingGateway : RefundStatus.ApprovedAwaitingPayout;
        var approved = await refunds.TryTransitionAsync(refund.Id, RefundStatus.PendingApproval, next, new RefundChanges(
            DecidedBy: manager.UserId, DecidedAt: now, DecisionNote: string.IsNullOrWhiteSpace(request?.Note) ? null : request.Note.Trim(),
            InvoiceId: invoice.Id, PaymentMethod: invoice.PaymentMethod, Channel: channel,
            Amount: invoice.TotalAmount, BhytReversalAmount: invoice.InsuranceDeduction,
            BhytReversalPostedAt: invoice.InsuranceDeduction > 0 ? now : null), ct)
            ?? throw NotPending(await LoadAsync(refund.Id, ct));
        await AuditAsync(manager, "REFUND_APPROVED", "Refund", approved.RefundCode,
            $"order {approved.OrderNumber}; {approved.Amount} via {channel} (invoice {invoice.InvoiceCode})", "WARNING", ct);
        if (invoice.InsuranceDeduction > 0)
            await AuditAsync(manager, "BHYT_REVERSAL_POSTED", "Invoice", invoice.InvoiceCode,
                $"BHYT {invoice.InsuranceDeduction} reversed for order {approved.OrderNumber} (not paid to the patient)", "INFO", ct);
        // TODO(phat, Đợt 4): VNPay refund API call (with the IPN work); until then a VNPay refund stays ApprovedAwaitingGateway.
        return Map(approved);
    }

    /// <inheritdoc />
    public async Task<RefundDto> RejectAsync(string refundId, RefundDecisionRequestDto request, Actor manager, CancellationToken ct = default)
    {
        var note = RequireReason(request?.Note);
        var refund = await LoadAsync(refundId, ct);
        var rejected = await refunds.TryTransitionAsync(refund.Id, RefundStatus.PendingApproval, RefundStatus.Rejected,
                           new RefundChanges(DecidedBy: manager.UserId, DecidedAt: clock.GetUtcNow().UtcDateTime, DecisionNote: note), ct)
                       ?? throw NotPending(await LoadAsync(refund.Id, ct));
        await AuditAsync(manager, "REFUND_REJECTED", "Refund", rejected.RefundCode, $"order {rejected.OrderNumber}; reason: {note}", "WARNING", ct);
        return Map(rejected);
    }

    /// <inheritdoc />
    public async Task<RefundDto> PayOutAtCounterAsync(string refundId, Actor receptionist, CancellationToken ct = default)
    {
        var refund = await LoadAsync(refundId, ct);
        if (refund.Channel == RefundChannel.VnPay)
            throw new ConflictStateException("REFUND_CHANNEL_VNPAY", "Bệnh nhân đã trả qua VNPay nên phải hoàn qua VNPay, không hoàn tại quầy.");
        if (refund.Status != RefundStatus.ApprovedAwaitingPayout)
            throw refund.Status == RefundStatus.PendingApproval
                ? new ConflictStateException("REFUND_NOT_APPROVED", "Yêu cầu hoàn tiền chưa được Quản lý duyệt.")
                : new ConflictStateException("REFUND_NOT_PAYABLE", "Yêu cầu hoàn tiền không ở trạng thái chờ chi tại quầy.");
        var now = clock.GetUtcNow().UtcDateTime;
        var done = await refunds.TryTransitionAsync(refund.Id, RefundStatus.ApprovedAwaitingPayout, RefundStatus.Completed,
                       new RefundChanges(PaidOutBy: receptionist.UserId, PaidOutAt: now), ct)
                   ?? throw new ConflictStateException("REFUND_NOT_PAYABLE", "Khoản hoàn này vừa được chi, vui lòng tải lại.");
        if (done.InvoiceId != null) await invoices.TryMarkRefundedAsync(done.InvoiceId, InvoiceStatus.Refunded, ct);
        await AuditAsync(receptionist, "REFUND_PAID_OUT", "Refund", done.RefundCode,
            $"order {done.OrderNumber}; {done.Amount} returned at counter ({done.PaymentMethod})", "WARNING", ct);
        return Map(done);
    }

    /// <inheritdoc />
    public async Task<RefundDto> GetAsync(string refundId, CancellationToken ct = default) => Map(await LoadAsync(refundId, ct));

    /// <inheritdoc />
    public async Task<IReadOnlyList<RefundDto>> ListAsync(string? status, CancellationToken ct = default)
    {
        RefundStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RefundStatus>(status.Trim(), true, out var s) || !Enum.IsDefined(s))
                throw new DomainValidationException("REFUND_STATUS_INVALID", "Trạng thái hoàn tiền không hợp lệ.");
            parsed = s;
        }
        return (await refunds.ListAsync(parsed, ct)).Select(Map).ToList();
    }

    /// <summary>Channel = the one the patient paid with: VNPay → VNPay; cash, POS/card and anything at the desk → counter.</summary>
    public static RefundChannel ChannelOf(PaymentMethod? method) => method == PaymentMethod.VNPay ? RefundChannel.VnPay : RefundChannel.Counter;

    private async Task<Refund> NewRefundAsync(TestOrder order, RefundKind kind, Actor actor, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var day = ClinicTime.LocalDate(now);
        var seq = await sequences.NextAsync($"rf:{day:yyyy-MM-dd}", ct);
        var invoice = await invoices.GetPaidByOrderIdAsync(order.Id, ct); // snapshot for display; approval re-resolves
        return new Refund
        {
            RefundCode = $"RF-{day:yyyyMMdd}-{seq:D4}",
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            PatientId = order.PatientId,
            Kind = kind,
            Status = RefundStatus.PendingApproval,
            Reason = reason,
            InvoiceId = invoice?.Id,
            PaymentMethod = invoice?.PaymentMethod,
            Channel = invoice == null ? null : ChannelOf(invoice.PaymentMethod),
            Amount = invoice?.TotalAmount ?? 0m,
            BhytReversalAmount = invoice?.InsuranceDeduction ?? 0m,
            RequestedBy = actor.UserId,
            RequestedAt = now
        };
    }

    private async Task<Refund> LoadAsync(string id, CancellationToken ct) =>
        (string.IsNullOrWhiteSpace(id) ? null : await refunds.GetAsync(id.Trim(), ct))
        ?? throw new ResourceNotFoundException("REFUND_NOT_FOUND", "Không tìm thấy yêu cầu hoàn tiền.");

    private static ConflictStateException NotPending(Refund r) =>
        new("REFUND_NOT_PENDING", $"Yêu cầu hoàn tiền đã được xử lý ({r.Status}).");

    private static string RequireReason(string? reason)
    {
        var r = reason?.Trim();
        if (r == null || r.Length < MinReasonLength)
            throw new DomainValidationException("REFUND_REASON_REQUIRED", $"Vui lòng ghi lý do (tối thiểu {MinReasonLength} ký tự).");
        return r;
    }

    private Task AuditAsync(Actor actor, string action, string target, string entityId, string details, string severity, CancellationToken ct) =>
        audit.LogAsync(new CreateAuditLogDto
        {
            UserId = actor.UserId, UserName = actor.Name, UserRole = actor.Role, Action = action, Category = "DATA_CHANGE",
            Severity = severity, TargetEntity = target, EntityId = entityId, Details = details
        }, ct);

    private static RefundDto Map(Refund r) => new()
    {
        Id = r.Id, RefundCode = r.RefundCode, OrderId = r.OrderId, OrderNumber = r.OrderNumber, PatientId = r.PatientId,
        Kind = r.Kind.ToString(), Status = r.Status.ToString(), Reason = r.Reason, InvoiceId = r.InvoiceId,
        PaymentMethod = r.PaymentMethod?.ToString(), Channel = r.Channel?.ToString(), Amount = r.Amount,
        BhytReversalAmount = r.BhytReversalAmount, BhytReversalPostedAt = r.BhytReversalPostedAt, RequestedBy = r.RequestedBy,
        RequestedAt = r.RequestedAt, DecidedBy = r.DecidedBy, DecidedAt = r.DecidedAt, DecisionNote = r.DecisionNote,
        PaidOutBy = r.PaidOutBy, PaidOutAt = r.PaidOutAt
    };
}
