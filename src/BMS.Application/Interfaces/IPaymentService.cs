using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Billing;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for patient billing payment operations (UC55 / Process Payment).
/// Supports VNPay, MoMo, Banking QR, and Pay-at-reception workflows.
/// </summary>
public interface IPaymentService
{
    Task<PaymentCheckoutDto?> GetCheckoutSummaryAsync(string invoiceId, CancellationToken ct = default);
    Task<DiscountResultDto> ApplyDiscountAsync(ApplyDiscountRequestDto request, CancellationToken ct = default);
    Task<PaymentCheckoutDto> ProcessPaymentAsync(ProcessPaymentRequestDto request, string? userId, string? userName, CancellationToken ct = default);
    Task<List<PaymentCheckoutDto>> GetPatientInvoicesAsync(string patientId, CancellationToken ct = default);
}
