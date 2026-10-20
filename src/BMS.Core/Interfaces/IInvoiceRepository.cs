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
    Task<Invoice?> IncrementPrintCountAsync(string id, string? cashierId, string? cashierName, CancellationToken ct = default);
}
