using System.Security.Cryptography;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <summary>
/// Payment records and receipt generation (Generate Receipt, I3-019).
/// The calculation previously lived in the payment_service PaymentsController; it is moved here so
/// the controller stays thin.
/// </summary>
public sealed class PaymentRecordService(IPaymentRepository repo, ILogger<PaymentRecordService> logger) : IPaymentRecordService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase) { "Pending", "Success", "Failed" };
    private const string ReceiptAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <inheritdoc />
    public Task<Payment> CreateAsync(CreatePaymentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PatientId))
            throw new DomainValidationException("PAYMENT_PATIENT_REQUIRED", "PatientId is required.");

        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            PatientId = request.PatientId.Trim(),
            Amount = request.Amount,
            Notes = request.Notes,
            Status = "Pending",
            PaidAt = request.PaidAt,
            CreatedAt = request.CreatedAt != default ? request.CreatedAt : DateTime.UtcNow,
            TransactionCode = request.TransactionCode,
            TxnRef = request.TxnRef,
        };
        return repo.CreateAsync(payment, ct);
    }

    /// <inheritdoc />
    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default) => repo.GetByIdAsync(id, ct);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Payment> Items, long Total)> GetAllPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        ValidatePaging(page, pageSize);
        var items = await repo.GetAllPagedAsync(page, pageSize, ct);
        var total = await repo.CountAllAsync(ct);
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Payment> Items, long Total)> GetByPatientPagedAsync(string patientId, int page, int pageSize, CancellationToken ct = default)
    {
        ValidatePaging(page, pageSize);
        var items = await repo.GetByPatientIdPagedAsync(patientId, page, pageSize, ct);
        var total = await repo.CountByPatientIdAsync(patientId, ct);
        return (items, total);
    }

    /// <inheritdoc />
    public Task<Payment?> UpdateStatusAsync(Guid id, UpdatePaymentStatusRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Status) || !AllowedStatuses.Contains(request.Status.Trim()))
            throw new DomainValidationException("PAYMENT_STATUS_INVALID", "Status must be Pending, Success or Failed.");
        // Atomic $set instead of read-modify-replace.
        return repo.UpdateStatusAsync(id, request.Status.Trim(), request.TransactionCode, request.TxnRef, ct);
    }

    /// <inheritdoc />
    public async Task<ReceiptDto> GenerateReceiptAsync(GenerateReceiptRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PatientId))
            throw new DomainValidationException("PAYMENT_PATIENT_REQUIRED", "PatientId is required.");

        // Calculate item amounts and subtotal
        // TODO(Đợt 3): unit prices come from the client; they should be looked up from test packages server-side.
        var items = new List<ReceiptItemDto>();
        decimal subTotal = 0;
        foreach (var item in request.Items ?? [])
        {
            if (item.UnitPrice < 0)
                throw new DomainValidationException("RECEIPT_PRICE_INVALID", "Unit price cannot be negative.");
            var quantity = item.Quantity > 0 ? item.Quantity : 1;
            var itemAmount = quantity * item.UnitPrice;
            subTotal += itemAmount;
            items.Add(new ReceiptItemDto
            {
                PackageId = item.PackageId,
                PackageName = item.PackageName,
                Quantity = quantity,
                UnitPrice = item.UnitPrice,
                Amount = itemAmount
            });
        }

        var taxPercent = request.TaxPercent.GetValueOrDefault(0);
        if (taxPercent is < 0 or > 100)
            throw new DomainValidationException("RECEIPT_TAX_INVALID", "TaxPercent must be between 0 and 100.");
        var taxAmount = Math.Round(subTotal * taxPercent / 100, 2);
        var discountAmount = Math.Round(Math.Max(0, request.DiscountAmount.GetValueOrDefault(0)), 2);
        var totalAmount = Math.Max(0, subTotal + taxAmount - discountAmount);

        var now = DateTime.UtcNow;
        var receiptNumber = $"REC-{now:yyyyMMdd}-{RandomNumberGenerator.GetString(ReceiptAlphabet, 6)}";

        // Persist payment transaction record
        await repo.CreateAsync(new Payment
        {
            PaymentId = Guid.NewGuid(),
            PatientId = request.PatientId.Trim(),
            Amount = totalAmount,
            Notes = $"Receipt {receiptNumber} processed by {request.ReceptionistName ?? "Receptionist"} for Appointment {request.AppointmentId ?? "N/A"}",
            Status = "Success",
            PaidAt = now,
            CreatedAt = now,
            TransactionCode = receiptNumber,
            TxnRef = receiptNumber
        }, ct);

        logger.LogInformation("Receipt {ReceiptNumber} generated, total {Total}", receiptNumber, totalAmount);

        return new ReceiptDto
        {
            ReceiptNumber = receiptNumber,
            AppointmentId = request.AppointmentId,
            PatientId = request.PatientId,
            PatientName = request.PatientName,
            PatientPhone = request.PatientPhone,
            ReceptionistId = request.ReceptionistId,
            ReceptionistName = request.ReceptionistName,
            PaymentMethod = request.PaymentMethod ?? "Cash",
            Status = "Paid",
            ReceiptDate = now,
            Items = items,
            SubTotal = subTotal,
            TaxPercent = taxPercent,
            TaxAmount = taxAmount,
            DiscountAmount = discountAmount,
            TotalAmount = totalAmount,
            Notes = request.Notes
        };
    }

    /// <inheritdoc />
    public Task<Payment?> GetByReceiptNumberAsync(string receiptNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber))
            throw new DomainValidationException("RECEIPT_NUMBER_REQUIRED", "Receipt number is required.");
        return repo.GetByReferenceAsync(receiptNumber.Trim(), ct);
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page <= 0 || pageSize <= 0 || pageSize > 200)
            throw new DomainValidationException("PAGING_INVALID", "Page must be > 0 and pageSize between 1 and 200.");
    }
}
