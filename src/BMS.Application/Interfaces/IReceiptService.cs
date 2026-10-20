using BMS.Application.DTOs.Billing;
using BMS.Core.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for Medical Billing, POS Checkout &amp; Receipt Printing (UC43).
/// </summary>
public interface IReceiptService
{
    Task<ReceiptPrintDataDto> GeneratePrintDataAsync(string invoiceId, string cashierId, string? cashierName, CancellationToken ct = default);
    Task<ReceiptVerificationResultDto> VerifyReceiptAsync(string receiptNumber, CancellationToken ct = default);
    Task<Invoice> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken ct = default);
    Task<Invoice> PayInvoiceAsync(string invoiceId, PayInvoiceRequest request, string cashierId, string? cashierName, CancellationToken ct = default);
    Task<List<Invoice>> GetInvoicesAsync(string? patientId, InvoiceStatus? status, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<Invoice?> GetByIdAsync(string id, CancellationToken ct = default);
}
