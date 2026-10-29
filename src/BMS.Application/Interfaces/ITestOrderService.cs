using BMS.Application.DTOs.Laboratory;

namespace BMS.Application.Interfaces;

/// <summary>
/// Test orders (UC32): doctors create orders with ICD-10, prices come from the catalog, the doctor may cancel
/// only their own order before sampling; other transitions are made by payment/sampling/results.
/// </summary>
public interface ITestOrderService
{
    /// <summary>Active catalog packages with server prices and tube hints.</summary>
    Task<IReadOnlyList<TestCatalogItemDto>> GetAvailableCatalogAsync(CancellationToken ct = default);

    /// <summary>Orders filtered by patient, doctor and/or status, newest first.</summary>
    Task<IReadOnlyList<TestOrderResponseDto>> GetOrdersAsync(string? patientId = null, string? doctorId = null, string? status = null, CancellationToken ct = default);

    /// <summary>Order by id or ORD- number, or null.</summary>
    Task<TestOrderResponseDto?> GetOrderByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Creates an order for the authenticated doctor (<paramref name="doctorUserId"/> from the JWT).</summary>
    Task<TestOrderResponseDto> CreateTestOrderAsync(CreateTestOrderRequestDto request, string doctorUserId, CancellationToken ct = default);

    /// <summary>
    /// Submits the doctor's own draft (Draft → AwaitingPayment; STAT → PaymentDeferred). ICD-10 becomes mandatory here
    /// (stored codes merged with the request): none or malformed → 400 MSG-ERR-DOC-01; not a draft → 409 ORDER_NOT_DRAFT;
    /// another doctor's draft → 403.
    /// </summary>
    Task<TestOrderResponseDto> SubmitDraftAsync(string id, SubmitTestOrderRequestDto request, string doctorUserId, CancellationToken ct = default);

    /// <summary>
    /// User-requested status change. Only Cancelled is allowed: Doctor = own order before sampling (else 403/409),
    /// Manager = exception cancel of any unfinished order.
    /// </summary>
    Task<TestOrderResponseDto> UpdateOrderStatusAsync(string id, UpdateTestOrderStatusRequestDto request, string actorUserId,
        bool actorIsManager, CancellationToken ct = default);
}
