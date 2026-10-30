using BMS.Core.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for Invoice and POS Receipt operations (UC43 / UC23).
/// </summary>
public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<Invoice?> GetByInvoiceCodeAsync(string invoiceCode, CancellationToken ct = default);
    Task<Invoice?> GetByReceiptNumberAsync(string receiptNumber, CancellationToken ct = default);
    Task<List<Invoice>> GetInvoicesAsync(string? patientId, InvoiceStatus? status, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<long> CountInvoicesAsync(string? patientId, InvoiceStatus? status, DateTime? fromDate, DateTime? toDate, CancellationToken ct = default);
    Task<Invoice> CreateAsync(Invoice invoice, CancellationToken ct = default);
    Task<Invoice?> UpdateAsync(Invoice invoice, CancellationToken ct = default);
    /// <summary>The Paid invoice of a test order (what the patient actually paid), or null.</summary>
    Task<Invoice?> GetPaidByOrderIdAsync(string orderId, CancellationToken ct = default);
    /// <summary>Atomically Paid → <paramref name="to"/> (Refunded); returns false when the invoice is no longer Paid.</summary>
    Task<bool> TryMarkRefundedAsync(string invoiceId, InvoiceStatus to, CancellationToken ct = default);
    /// <summary>Voids the Unpaid invoices of an order (pending VNPay) so they can never be settled after a cancel.</summary>
    Task<long> VoidUnpaidByOrderIdAsync(string orderId, CancellationToken ct = default);
    Task<Invoice?> IncrementPrintCountAsync(string id, string? cashierId, string? cashierName, CancellationToken ct = default);
}
