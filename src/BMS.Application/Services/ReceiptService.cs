using BMS.Application.Common;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Services;

/// <summary>
/// Service implementing Medical Billing checkout and POS Receipt printing (UC43).
/// Enforces 'Paid' invariant, atomic print counter increments, and Vietnamese number-to-words conversion.
/// </summary>
public class ReceiptService : IReceiptService
{
    private readonly IInvoiceRepository _invoiceRepository;

    public ReceiptService(IInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<ReceiptPrintDataDto> GeneratePrintDataAsync(
        string invoiceId,
        string cashierId,
        string? cashierName,
        CancellationToken ct = default)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, ct)
            ?? throw new KeyNotFoundException($"Không tìm thấy hóa đơn với mã '{invoiceId}'.");

        // Strict clinical billing invariant: Only invoices with status Paid can be printed as receipts.
        if (invoice.Status != InvoiceStatus.Paid)
        {
            throw new InvalidOperationException(
                $"Chỉ có thể in biên lai thu phí cho hóa đơn đã thanh toán thành công (Trạng thái hiện tại: {invoice.Status}). " +
                "Vui lòng hoàn tất thanh toán trước khi in biên lai.");
        }

        // Atomically increment print count to prevent double-charging and track duplicates.
        var updatedInvoice = await _invoiceRepository.IncrementPrintCountAsync(invoice.Id, cashierId, cashierName, ct)
            ?? invoice;

        var amountInWords = VietnameseCurrencyReader.ToVietnameseWords(updatedInvoice.TotalAmount);

        var paymentMethodStr = updatedInvoice.PaymentMethod switch
        {
            PaymentMethod.Cash => "Tiền mặt (Cash)",
            PaymentMethod.VNPay => "VNPay QR",
            PaymentMethod.BankTransfer => "Chuyển khoản Ngân hàng",
            PaymentMethod.CreditCard => "Thẻ tín dụng / Ghi nợ (POS)",
            _ => "Tiền mặt (Cash)"
        };

        var verificationUrl = $"https://bms.med.vn/verify-receipt?receipt={updatedInvoice.ReceiptNumber}&inv={updatedInvoice.InvoiceCode}&amt={updatedInvoice.TotalAmount}";
        var qrPayload = $"BMS-REC|{updatedInvoice.ReceiptNumber}|{updatedInvoice.InvoiceCode}|{updatedInvoice.PatientName}|{updatedInvoice.TotalAmount}|{updatedInvoice.PaidAt:yyyyMMddHHmmss}";

        var printDto = new ReceiptPrintDataDto
        {
            ReceiptNumber = updatedInvoice.ReceiptNumber ?? $"REC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            InvoiceId = updatedInvoice.Id,
            InvoiceCode = updatedInvoice.InvoiceCode,
            PrintedAt = updatedInvoice.LastPrintedAt ?? DateTime.UtcNow,
            CashierId = updatedInvoice.CashierId ?? cashierId,
            CashierName = updatedInvoice.CashierName ?? (string.IsNullOrWhiteSpace(cashierName) ? "Thu ngân BMS" : cashierName),
            PrintCount = updatedInvoice.PrintCount,
            PatientId = updatedInvoice.PatientId,
            PatientName = updatedInvoice.PatientName ?? "Khách hàng",
            PatientPhone = updatedInvoice.PatientPhone,
            AppointmentId = updatedInvoice.AppointmentId,
            Items = updatedInvoice.Items.Select(x => new ReceiptPrintItemDto
            {
                ItemCode = x.ItemCode,
                ItemName = x.ItemName,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice,
                TotalPrice = x.TotalPrice
            }).ToList(),
            SubTotal = updatedInvoice.SubTotal,
            DiscountAmount = updatedInvoice.DiscountAmount,
            TotalAmount = updatedInvoice.TotalAmount,
            TotalAmountInWords = amountInWords,
            PaymentMethod = paymentMethodStr,
            TransactionReference = updatedInvoice.TransactionReference,
            PaidAt = updatedInvoice.PaidAt,
            VerificationUrl = verificationUrl,
            VerificationQrPayload = qrPayload
        };

        return printDto;
    }

    public async Task<ReceiptVerificationResultDto> VerifyReceiptAsync(string receiptNumber, CancellationToken ct = default)
    {
        var invoice = await _invoiceRepository.GetByReceiptNumberAsync(receiptNumber, ct)
            ?? await _invoiceRepository.GetByInvoiceCodeAsync(receiptNumber, ct);

        if (invoice == null)
        {
            return new ReceiptVerificationResultDto
            {
                IsValid = false,
                ReceiptNumber = receiptNumber,
                Status = "NotFound"
            };
        }

        return new ReceiptVerificationResultDto
        {
            IsValid = invoice.Status == InvoiceStatus.Paid,
            ReceiptNumber = invoice.ReceiptNumber ?? invoice.InvoiceCode,
            InvoiceCode = invoice.InvoiceCode,
            PatientName = invoice.PatientName ?? "Khách hàng",
            TotalAmount = invoice.TotalAmount,
            TotalAmountInWords = VietnameseCurrencyReader.ToVietnameseWords(invoice.TotalAmount),
            PaidAt = invoice.PaidAt,
            Status = invoice.Status.ToString(),
            PrintCount = invoice.PrintCount
        };
    }

    public async Task<Invoice> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken ct = default)
    {
        var subTotal = request.Items.Sum(x => x.TotalPrice);
        var total = Math.Max(0, subTotal - request.DiscountAmount);

        var invoice = new Invoice
        {
            InvoiceCode = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            PatientId = request.PatientId,
            PatientName = request.PatientName,
            PatientPhone = request.PatientPhone,
            AppointmentId = request.AppointmentId,
            Items = request.Items,
            SubTotal = subTotal,
            DiscountAmount = request.DiscountAmount,
            TotalAmount = total,
            Status = InvoiceStatus.Unpaid,
            PaymentMethod = request.PaymentMethod,
            CreatedAt = DateTime.UtcNow
        };

        return await _invoiceRepository.CreateAsync(invoice, ct);
    }

    public async Task<Invoice> PayInvoiceAsync(
        string invoiceId,
        PayInvoiceRequest request,
        string cashierId,
        string? cashierName,
        CancellationToken ct = default)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, ct)
            ?? throw new KeyNotFoundException($"Không tìm thấy hóa đơn với mã '{invoiceId}'.");

        if (invoice.Status == InvoiceStatus.Paid)
        {
            return invoice; // Already paid
        }

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = DateTime.UtcNow;
        invoice.PaymentMethod = request.PaymentMethod;
        invoice.TransactionReference = request.TransactionReference;
        invoice.CashierId = cashierId;
        invoice.CashierName = cashierName;
        invoice.ReceiptNumber = $"REC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var updated = await _invoiceRepository.UpdateAsync(invoice, ct);
        return updated ?? invoice;
    }

    public async Task<List<Invoice>> GetInvoicesAsync(
        string? patientId,
        InvoiceStatus? status,
        DateTime? fromDate,
        DateTime? toDate,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        return await _invoiceRepository.GetInvoicesAsync(patientId, status, fromDate, toDate, page, pageSize, ct);
    }

    public async Task<Invoice?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await _invoiceRepository.GetByIdAsync(id, ct);
    }
}
