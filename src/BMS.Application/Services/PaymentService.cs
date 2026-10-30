using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Application.Services.Billing;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// UC55 invoice checkout. Voucher numbers come from <see cref="FeeCalculator"/> (same as UC53: BMS10 capped at
/// 200,000, rounding to 1,000); online methods never mark an invoice Paid without a verified gateway callback.
/// </summary>
/// <remarks>WHY changed (Đợt 3): BMS10 had no cap here (UC55 ≠ UC53), unknown codes such as SPRING10/HEALTH2026 gave
/// discounts, VNPay/MoMo were set Paid immediately with a fake QR CRC, and invoices were mirrored into BmsDbContext.</remarks>
public class PaymentService(IInvoiceRepository invoiceRepo, ITestOrderRepository orders, TimeProvider clock) : IPaymentService
{
    public async Task<PaymentCheckoutDto?> GetCheckoutSummaryAsync(string invoiceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            return null;
        }

        var invoice = await FindInvoiceAsync(invoiceId.Trim(), ct);
        if (invoice == null)
        {
            return null;
        }

        return MapToCheckoutDto(invoice);
    }

    public async Task<DiscountResultDto> ApplyDiscountAsync(ApplyDiscountRequestDto request, CancellationToken ct = default)
    {
        var invoice = await FindInvoiceAsync(request.InvoiceId.Trim(), ct)
                      ?? throw new ResourceNotFoundException("INVOICE_NOT_FOUND", "Không tìm thấy hóa đơn.");
        var code = request.DiscountCode.Trim().ToUpperInvariant();
        var (valid, discount, description) = FeeCalculator.Voucher(code, invoice.SubTotal, 0m);
        return new DiscountResultDto
        {
            IsValid = valid,
            DiscountCode = code,
            DiscountPercent = valid && code == "BMS10" ? 10m : 0m,
            DiscountAmount = discount,
            OriginalSubTotal = invoice.SubTotal,
            NewTotal = FeeCalculator.RoundToThousand(Math.Max(0m, invoice.SubTotal - discount)),
            Message = description
        };
    }

    public async Task<PaymentCheckoutDto> ProcessPaymentAsync(
        ProcessPaymentRequestDto request,
        string? userId,
        string? userName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.InvoiceId))
            throw new DomainValidationException("INVOICE_REQUIRED", "Vui lòng chọn hóa đơn.");
        var invoice = await FindInvoiceAsync(request.InvoiceId.Trim(), ct)
                      ?? throw new ResourceNotFoundException("INVOICE_NOT_FOUND", "Không tìm thấy hóa đơn.");
        if (invoice.Status != InvoiceStatus.Unpaid)
            throw new ConflictStateException("INVOICE_NOT_PAYABLE", "Hóa đơn đã thanh toán hoặc đã hủy.");

        string? appliedDiscountCode = null;
        if (!string.IsNullOrWhiteSpace(request.DiscountCode))
        {
            var (valid, discount, _) = FeeCalculator.Voucher(request.DiscountCode, invoice.SubTotal, 0m);
            if (valid)
            {
                appliedDiscountCode = request.DiscountCode.Trim().ToUpperInvariant();
                invoice.DiscountAmount = discount;
                invoice.TotalAmount = FeeCalculator.RoundToThousand(Math.Max(0m, invoice.SubTotal - discount));
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var method = (request.PaymentMethod ?? string.Empty).Trim().ToUpperInvariant();
        string message;
        switch (method)
        {
            case "CASH":
            case "POS":
            case "CARD":
            case "CREDITCARD":
                invoice.PaymentMethod = method == "CASH" ? PaymentMethod.Cash : PaymentMethod.CreditCard;
                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidAt = now;
                invoice.CashierId = userId;
                invoice.CashierName = userName;
                invoice.TransactionReference = $"RCP-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                message = $"Đã thu {invoice.TotalAmount:N0}đ.";
                break;
            case "RECEPTION":
            case "PAY AT RECEPTION":
                invoice.PaymentMethod = PaymentMethod.Reception;
                message = "Thanh toán tại quầy khi đến phòng khám.";
                break;
            case "VNPAY":
            case "MOMO":
            case "BANKINGQR":
            case "BANKING QR":
                // WHY still Unpaid: only a verified gateway callback may mark Paid (GAP C3.3). TODO(phat, Đợt 4): IPN.
                invoice.PaymentMethod = method == "MOMO" ? PaymentMethod.MoMo : method == "VNPAY" ? PaymentMethod.VNPay : PaymentMethod.BankingQR;
                message = "Đang chờ xác nhận từ cổng thanh toán.";
                break;
            default:
                throw new DomainValidationException("PAYMENT_METHOD_INVALID", "Phương thức thanh toán không hợp lệ.");
        }

        await invoiceRepo.UpdateAsync(invoice, ct);
        if (invoice.Status == InvoiceStatus.Paid && !string.IsNullOrWhiteSpace(invoice.OrderId))
            await orders.TryMarkPaidAsync(invoice.OrderId, userId ?? string.Empty, now, ct);

        var dto = MapToCheckoutDto(invoice);
        dto.DiscountCode = appliedDiscountCode;
        dto.Message = message;
        dto.QrCodePayload = null;
        dto.PaymentUrl = null;
        return dto;
    }

    public async Task<List<PaymentCheckoutDto>> GetPatientInvoicesAsync(string patientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return new List<PaymentCheckoutDto>();
        }

        var list = await invoiceRepo.GetInvoicesAsync(patientId.Trim(), null, null, null, 1, 50, ct);
        return list.Select(MapToCheckoutDto).ToList();
    }

    /// <inheritdoc />
    public async Task<string?> GetInvoicePatientIdAsync(string invoiceIdOrCode, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(invoiceIdOrCode) ? null : (await FindInvoiceAsync(invoiceIdOrCode, ct))?.PatientId;

    private async Task<Invoice?> FindInvoiceAsync(string invoiceIdOrCode, CancellationToken ct)
    {
        var key = invoiceIdOrCode.Trim();

        // 1. Check via repository (MongoDB collection 'invoices')
        var found = await invoiceRepo.GetByIdAsync(key, ct);
        if (found != null) return found;

        found = await invoiceRepo.GetByInvoiceCodeAsync(key, ct);
        if (found != null) return found;

        return null;
    }

    private static PaymentCheckoutDto MapToCheckoutDto(Invoice inv)
    {
        return new PaymentCheckoutDto
        {
            InvoiceId = inv.Id,
            InvoiceCode = inv.InvoiceCode,
            PatientId = inv.PatientId,
            PatientName = inv.PatientName ?? "Patient",
            PatientPhone = inv.PatientPhone,
            AppointmentId = inv.AppointmentId,
            Items = inv.Items ?? new List<InvoiceItem>(),
            SubTotal = inv.SubTotal,
            ServiceFee = 0,
            DiscountAmount = inv.DiscountAmount,
            TotalAmount = inv.TotalAmount,
            Status = inv.Status.ToString(),
            PaymentMethod = inv.PaymentMethod?.ToString(),
            TransactionReference = inv.TransactionReference,
            PaidAt = inv.PaidAt,
            CreatedAt = inv.CreatedAt,
            Message = inv.Status == InvoiceStatus.Paid ? "Invoice is settled." : "Invoice awaiting settlement."
        };
    }
}
