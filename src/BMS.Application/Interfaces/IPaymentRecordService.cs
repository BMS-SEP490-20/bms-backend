using BMS.Application.DTOs.Billing;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

/// <summary>
/// Payment transaction records and receipt generation (Generate Receipt, I3-019).
/// Separate from <see cref="IPaymentService"/> (UC55 invoice checkout), which works on invoices.
/// </summary>
public interface IPaymentRecordService
{
    /// <summary>Records a new pending payment.</summary>
    Task<Payment> CreateAsync(CreatePaymentRequest request, CancellationToken ct = default);
    /// <summary>Gets a payment by id, or null.</summary>
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns one page of all payments and the total count.</summary>
    Task<(IReadOnlyList<Payment> Items, long Total)> GetAllPagedAsync(int page, int pageSize, CancellationToken ct = default);
    /// <summary>Returns one page of a patient's payments and the total count.</summary>
    Task<(IReadOnlyList<Payment> Items, long Total)> GetByPatientPagedAsync(string patientId, int page, int pageSize, CancellationToken ct = default);
    /// <summary>Updates status / references; returns null when the payment does not exist.</summary>
    Task<Payment?> UpdateStatusAsync(Guid id, UpdatePaymentStatusRequest request, CancellationToken ct = default);
    /// <summary>Calculates totals, persists the payment and returns the receipt.</summary>
    Task<ReceiptDto> GenerateReceiptAsync(GenerateReceiptRequest request, CancellationToken ct = default);
    /// <summary>Finds the payment behind a receipt number / transaction code, or null.</summary>
    Task<Payment?> GetByReceiptNumberAsync(string receiptNumber, CancellationToken ct = default);
}
