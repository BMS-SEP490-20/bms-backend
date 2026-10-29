using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Application.Services.Billing;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <inheritdoc />
public sealed class OrderPaymentService(
    ITestOrderRepository orders,
    IInvoiceRepository invoices,
    IPatientRepository patients,
    ISequenceGenerator sequences,
    IFeePolicyProvider policies,
    TimeProvider clock,
    ILogger<OrderPaymentService> logger) : IOrderPaymentService
{
    /// <inheritdoc />
    public async Task<CalculateFeeResultDto> QuoteAsync(string orderId, OrderPaymentRequestDto request, CancellationToken ct = default)
    {
        var order = await LoadAsync(orderId, ct);
        return await ComputeAsync(order, request, ct);
    }

    /// <inheritdoc />
    public async Task<OrderPaymentResultDto> PayAtCounterAsync(string orderId, OrderPaymentRequestDto request, string cashierId,
        string? cashierName, CancellationToken ct = default)
    {
        var method = (request.PaymentMethod ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "CASH" or "TIỀN MẶT" => PaymentMethod.Cash,
            "POS" or "CARD" or "CREDITCARD" => PaymentMethod.CreditCard,
            _ => throw new DomainValidationException("PAYMENT_METHOD_INVALID", "Tại quầy chỉ nhận tiền mặt hoặc POS; VNPay dùng cổng trực tuyến.")
        };
        var order = await LoadAsync(orderId, ct);
        EnsurePayable(order);
        var fee = await ComputeAsync(order, request, ct); // validates the card/voucher before anything is written
        var now = clock.GetUtcNow().UtcDateTime;

        // WHY mark the order first: the conditional update is the single "who pays" decision; a second cashier
        // (or a double click) gets 409 and no second invoice is written.
        var paid = await orders.TryMarkPaidAsync(order.Id, cashierId, now, ct)
                   ?? throw new ConflictStateException("ORDER_ALREADY_PAID", "Đơn đã được thanh toán hoặc không còn ở trạng thái chờ thanh toán.");

        var invoice = NewInvoice(order, fee, now, await NextCodeAsync("INV", now, ct));
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaymentMethod = method;
        invoice.PaidAt = now;
        invoice.CashierId = cashierId;
        invoice.CashierName = cashierName;
        invoice.ReceiptNumber = await NextCodeAsync("RCP", now, ct);
        invoice.TransactionReference = invoice.ReceiptNumber;
        await invoices.CreateAsync(invoice, ct);
        logger.LogInformation("Order {OrderNumber} paid at counter by {CashierId}: {Net} VND via {Method}, invoice {InvoiceCode}",
            paid.OrderNumber, cashierId, fee.NetTotalPayable, method, invoice.InvoiceCode);
        return Result(paid, invoice, fee, "Đã thu tiền. Đơn chuyển sang chờ lấy mẫu.");
    }

    /// <inheritdoc />
    public async Task<OrderPaymentResultDto> StartVnPayAsync(string orderId, OrderPaymentRequestDto request, string actorUserId,
        bool actorIsPatient, CancellationToken ct = default)
    {
        if (!string.Equals(request.PaymentMethod?.Trim(), "VNPay", StringComparison.OrdinalIgnoreCase))
        {
            if (actorIsPatient) throw new ForbiddenAccessException("Bệnh nhân chỉ được thanh toán trực tuyến qua VNPay.");
            throw new DomainValidationException("PAYMENT_METHOD_INVALID", "Cổng trực tuyến chỉ hỗ trợ VNPay.");
        }
        var order = await LoadAsync(orderId, ct);
        if (actorIsPatient)
        {
            var own = await patients.GetByAccountIdAsync(actorUserId, ct);
            if (own == null || !string.Equals(own.Id, order.PatientId, StringComparison.Ordinal))
                throw new ForbiddenAccessException("Bạn chỉ được thanh toán đơn của chính mình.");
        }
        EnsurePayable(order);
        var fee = await ComputeAsync(order, request, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var invoice = NewInvoice(order, fee, now, await NextCodeAsync("INV", now, ct));
        invoice.Status = InvoiceStatus.Unpaid;
        invoice.PaymentMethod = PaymentMethod.VNPay;
        await invoices.CreateAsync(invoice, ct);
        // WHY not Paid: without a verified VNPay IPN callback (signature + amount) nothing proves the money
        // arrived (GAP C3.3; the fake CRC "6304ABCD" QR was removed). TODO(phat, Đợt 4): VNPay IPN endpoint.
        logger.LogInformation("VNPay payment started for order {OrderNumber} by {ActorId}: invoice {InvoiceCode} pending",
            order.OrderNumber, actorUserId, invoice.InvoiceCode);
        return Result(order, invoice, fee, "Đã tạo yêu cầu thanh toán VNPay. Đơn chỉ chuyển sang đã thanh toán khi có xác nhận từ VNPay.");
    }

    private async Task<CalculateFeeResultDto> ComputeAsync(TestOrder order, OrderPaymentRequestDto r, CancellationToken ct) =>
        FeeCalculator.Compute(CalculateFeeService.OrderLines(order), r.HomeCollection, r.InsuranceCardNumber, r.VoucherCode, false,
            await policies.GetAsync(ct), clock.GetUtcNow().UtcDateTime);

    private async Task<TestOrder> LoadAsync(string orderId, CancellationToken ct) =>
        (string.IsNullOrWhiteSpace(orderId) ? null : await orders.GetByIdAsync(orderId, ct) ?? await orders.GetByOrderNumberAsync(orderId, ct))
        ?? throw new ResourceNotFoundException("ORDER_NOT_FOUND", "Không tìm thấy đơn xét nghiệm.");

    private static void EnsurePayable(TestOrder order)
    {
        if (order.PaidAt != null)
            throw new ConflictStateException("ORDER_ALREADY_PAID", "Đơn đã được thanh toán.");
        if (order.Status is TestOrderStatus.Cancelled or TestOrderStatus.Draft)
            throw new ConflictStateException("ORDER_NOT_PAYABLE", "Đơn đã hủy hoặc còn nháp, không thể thanh toán.");
    }

    private static Invoice NewInvoice(TestOrder order, CalculateFeeResultDto fee, DateTime now, string invoiceCode) => new()
    {
        InvoiceCode = invoiceCode,
        PatientId = order.PatientId,
        PatientName = order.PatientName,
        OrderId = order.Id,
        Items = order.Items.Select(i => new InvoiceItem { ItemCode = i.TestCode, ItemName = i.TestName, Quantity = 1, UnitPrice = i.Price })
            .Concat(fee.HomeCollectionSurcharge > 0
                ? [new InvoiceItem { ItemCode = "HOME", ItemName = "Phụ phí lấy mẫu tại nhà", Quantity = 1, UnitPrice = fee.HomeCollectionSurcharge }]
                : []).ToList(),
        SubTotal = fee.GrossTotal,
        InsuranceDeduction = fee.InsuranceDeduction,
        DiscountAmount = fee.TotalDiscount,
        TotalAmount = fee.NetTotalPayable,
        CreatedAt = now
    };

    private async Task<string> NextCodeAsync(string prefix, DateTime now, CancellationToken ct)
    {
        var day = ClinicTime.LocalDate(now);
        var seq = await sequences.NextAsync($"{prefix.ToLowerInvariant()}:{day:yyyy-MM-dd}", ct);
        return $"{prefix}-{day:yyyyMMdd}-{seq:D4}";
    }

    private static OrderPaymentResultDto Result(TestOrder order, Invoice invoice, CalculateFeeResultDto fee, string message) => new()
    {
        OrderId = order.Id,
        OrderNumber = order.OrderNumber,
        OrderStatus = order.Status.ToString(),
        InvoiceId = invoice.Id,
        InvoiceCode = invoice.InvoiceCode,
        ReceiptNumber = invoice.ReceiptNumber,
        InvoiceStatus = invoice.Status.ToString(),
        Fee = fee,
        Message = message
    };
}
