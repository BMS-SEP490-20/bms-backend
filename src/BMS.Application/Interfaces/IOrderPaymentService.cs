using BMS.Application.DTOs.Billing;

namespace BMS.Application.Interfaces;

/// <summary>
/// Payment gate for test orders (BA:L38, TIEU-CHI Đợt 3 Bước 2): fee from the stored order (UC53 formula),
/// counter payment moves the order to PaidPendingSample, VNPay never marks Paid without a verified callback.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Fee preview for an order (Receptionist).</summary>
    Task<CalculateFeeResultDto> QuoteAsync(string orderId, OrderPaymentRequestDto request, CancellationToken ct = default);

    /// <summary>Cash/POS at the counter: invoice Paid + order PaidPendingSample, exactly once (409 if already paid).</summary>
    Task<OrderPaymentResultDto> PayAtCounterAsync(string orderId, OrderPaymentRequestDto request, string cashierId, string? cashierName,
        CancellationToken ct = default);

    /// <summary>
    /// Starts an online VNPay payment: creates an Unpaid invoice; the order stays AwaitingPayment.
    /// A Patient may only start it for their own order (403 otherwise).
    /// </summary>
    Task<OrderPaymentResultDto> StartVnPayAsync(string orderId, OrderPaymentRequestDto request, string actorUserId, bool actorIsPatient,
        CancellationToken ct = default);
}
