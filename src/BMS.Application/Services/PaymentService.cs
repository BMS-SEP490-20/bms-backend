using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Service implementing patient fee checkout and digital payment processing (UC55 / Process Payment).
/// Supports VNPay, MoMo e-Wallet, Banking QR, and Pay-at-reception workflows.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly BmsDbContext _dbContext;
    private readonly IMongoCollection<Invoice>? _invoicesCol;

    public PaymentService(
        IInvoiceRepository invoiceRepo,
        BmsDbContext dbContext,
        IMongoDatabase? database = null)
    {
        _invoiceRepo = invoiceRepo;
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _invoicesCol = database.GetCollection<Invoice>("invoices");
            }
            catch
            {
                _invoicesCol = null;
            }
        }
    }

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
        var invoice = await FindInvoiceAsync(request.InvoiceId.Trim(), ct);
        if (invoice == null)
        {
            return new DiscountResultDto
            {
                IsValid = false,
                Message = $"Invoice '{request.InvoiceId}' not found."
            };
        }

        var code = request.DiscountCode.Trim().ToUpperInvariant();
        decimal percent = code switch
        {
            "SPRING10" => 10m,
            "BMS10" => 10m,
            "HEALTH2026" => 15m,
            "BMS20" => 20m,
            _ => 0m
        };

        if (percent <= 0)
        {
            return new DiscountResultDto
            {
                IsValid = false,
                DiscountCode = code,
                OriginalSubTotal = invoice.SubTotal,
                NewTotal = invoice.TotalAmount,
                Message = "Invalid or expired discount code. Try SPRING10 for 10% off."
            };
        }

        var discountAmount = Math.Round(invoice.SubTotal * (percent / 100m), 0);
        var newTotal = Math.Max(0, invoice.SubTotal - discountAmount);

        return new DiscountResultDto
        {
            IsValid = true,
            DiscountCode = code,
            DiscountPercent = percent,
            DiscountAmount = discountAmount,
            OriginalSubTotal = invoice.SubTotal,
            NewTotal = newTotal,
            Message = $"{code} applied — {percent:0}% off order total"
        };
    }

    public async Task<PaymentCheckoutDto> ProcessPaymentAsync(
        ProcessPaymentRequestDto request,
        string? userId,
        string? userName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.InvoiceId))
        {
            throw new ArgumentException("Invoice identifier is required.", nameof(request.InvoiceId));
        }

        var invoice = await FindInvoiceAsync(request.InvoiceId.Trim(), ct);
        if (invoice == null)
        {
            throw new KeyNotFoundException($"Invoice '{request.InvoiceId}' not found in database.");
        }

        // Apply discount if provided
        string? appliedDiscountCode = null;
        if (!string.IsNullOrWhiteSpace(request.DiscountCode))
        {
            var code = request.DiscountCode.Trim().ToUpperInvariant();
            decimal percent = code switch
            {
                "SPRING10" => 10m,
                "BMS10" => 10m,
                "HEALTH2026" => 15m,
                "BMS20" => 20m,
                _ => 0m
            };

            if (percent > 0)
            {
                appliedDiscountCode = code;
                invoice.DiscountAmount = Math.Round(invoice.SubTotal * (percent / 100m), 0);
                invoice.TotalAmount = Math.Max(0, invoice.SubTotal - invoice.DiscountAmount);
            }
        }

        var methodStr = request.PaymentMethod.Trim();
        var methodUpper = methodStr.ToUpperInvariant();

        string confirmationMessage;
        string? qrCodePayload = null;
        string? paymentUrl = null;

        if (methodUpper == "RECEPTION" || methodUpper == "PAY AT RECEPTION")
        {
            invoice.PaymentMethod = PaymentMethod.Reception;
            invoice.Status = InvoiceStatus.Unpaid; // Patient will pay cash/card upon arrival
            invoice.TransactionReference = $"RECP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}";
            confirmationMessage = "Order confirmed for cash or POS card settlement upon arrival at clinic reception.";
        }
        else
        {
            // Digital settlement (VNPay, MoMo, BankingQR)
            var (parsedMethod, methodLabel) = methodUpper switch
            {
                "MOMO" => (PaymentMethod.MoMo, "MoMo e-Wallet"),
                "BANKINGQR" or "BANKING QR" => (PaymentMethod.BankingQR, "Vietnamese Banking QR"),
                "CASH" => (PaymentMethod.Cash, "Cash"),
                "CREDITCARD" or "CARD" or "POS" => (PaymentMethod.CreditCard, "Credit Card"),
                _ => (PaymentMethod.VNPay, "VNPay Gateway")
            };

            invoice.PaymentMethod = parsedMethod;
            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAt = DateTime.UtcNow;
            invoice.TransactionReference = $"TXN-{parsedMethod}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..6].ToUpper()}";

            confirmationMessage = $"Payment of {invoice.TotalAmount:N0} ₫ successfully processed via {methodLabel}.";
            qrCodePayload = $"00020101021238540010A0000007270124{invoice.InvoiceCode}5303704540{invoice.TotalAmount:0}5802VN62200816{invoice.TransactionReference}6304ABCD";
            paymentUrl = string.IsNullOrWhiteSpace(request.ReturnUrl) ? null : $"{request.ReturnUrl}?status=success&txn={invoice.TransactionReference}";
        }

        // Persist update in MongoDB and local in-memory store
        await _invoiceRepo.UpdateAsync(invoice, ct);

        lock (_dbContext.Invoices)
        {
            var existing = _dbContext.Invoices.FirstOrDefault(x => x.Id == invoice.Id || x.InvoiceCode == invoice.InvoiceCode);
            if (existing != null)
            {
                _dbContext.Invoices.Remove(existing);
            }
            _dbContext.Invoices.Add(invoice);
        }

        var dto = MapToCheckoutDto(invoice);
        dto.DiscountCode = appliedDiscountCode;
        dto.Message = confirmationMessage;
        dto.QrCodePayload = qrCodePayload;
        dto.PaymentUrl = paymentUrl;

        return dto;
    }

    public async Task<List<PaymentCheckoutDto>> GetPatientInvoicesAsync(string patientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return new List<PaymentCheckoutDto>();
        }

        var list = await _invoiceRepo.GetInvoicesAsync(patientId.Trim(), null, null, null, 1, 50, ct);
        return list.Select(MapToCheckoutDto).ToList();
    }

    /// <inheritdoc />
    public async Task<string?> GetInvoicePatientIdAsync(string invoiceIdOrCode, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(invoiceIdOrCode) ? null : (await FindInvoiceAsync(invoiceIdOrCode, ct))?.PatientId;

    private async Task<Invoice?> FindInvoiceAsync(string invoiceIdOrCode, CancellationToken ct)
    {
        var key = invoiceIdOrCode.Trim();

        // 1. Check via repository (MongoDB collection 'invoices')
        var found = await _invoiceRepo.GetByIdAsync(key, ct);
        if (found != null) return found;

        found = await _invoiceRepo.GetByInvoiceCodeAsync(key, ct);
        if (found != null) return found;

        // 2. Check BmsDbContext in-memory store
        lock (_dbContext.Invoices)
        {
            return _dbContext.Invoices.FirstOrDefault(i =>
                string.Equals(i.Id, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.InvoiceCode, key, StringComparison.OrdinalIgnoreCase));
        }
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
