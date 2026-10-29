using System.Security.Claims;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Clinical test orders (UC32). Thin controller: rules live in <see cref="ITestOrderService"/>; errors are
/// <c>AppException</c>s mapped to ProblemDetails (with <c>code</c>) by the global handler.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// WHY: UC32: create Doctor; list Doctor, Lab, Receptionist, Manager; status Doctor (cancel own) / Manager (exceptions). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class TestOrdersController(ITestOrderService testOrderService) : ControllerBase
{
    /// <summary>Active catalog with server prices and tube colors.</summary>
    [Authorize(Roles = RoleSets.ClinicalStaffOrReception)]
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(CancellationToken ct) =>
        Ok(new { success = true, data = await testOrderService.GetAvailableCatalogAsync(ct) });

    /// <summary>Orders filtered by patient, doctor or status (newest first, max 200).</summary>
    [Authorize(Roles = RoleSets.ClinicalStaffOrReception)]
    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] string? patientId, [FromQuery] string? doctorId, [FromQuery] string? status,
        CancellationToken ct)
    {
        var orders = await testOrderService.GetOrdersAsync(patientId, doctorId, status, ct);
        return Ok(new { success = true, total = orders.Count, data = orders });
    }

    /// <summary>Order by id or ORD- number.</summary>
    [Authorize(Roles = RoleSets.ClinicalStaffOrReception)]
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(string id, CancellationToken ct)
    {
        var order = await testOrderService.GetOrderByIdAsync(id, ct);
        return order == null
            ? NotFound(new { success = false, code = "ORDER_NOT_FOUND", message = "Không tìm thấy đơn xét nghiệm." })
            : Ok(new { success = true, data = order });
    }

    /// <summary>
    /// Creates an order for the signed-in doctor. 201 AwaitingPayment (STAT: PaymentDeferred = true);
    /// 400 MSG-ERR-DOC-01 when ICD-10 is missing/malformed; 400 when a test is not in the catalog.
    /// DoctorId and prices in the body are ignored.
    /// </summary>
    [Authorize(Roles = RoleNames.Doctor)]
    [HttpPost]
    [ProducesResponseType(typeof(TestOrderResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateTestOrderRequestDto request, CancellationToken ct)
    {
        var created = await testOrderService.CreateTestOrderAsync(request, CurrentUserId(), ct);
        return CreatedAtAction(nameof(GetOrderById), new { id = created.OrderNumber },
            new { success = true, message = $"Đã tạo phiếu chỉ định {created.OrderNumber}.", data = created });
    }

    /// <summary>
    /// Cancels an order (only "Cancelled" is accepted). Doctor: own order before sampling — other doctor's order 403,
    /// sample collected 409, any other target status 403. Manager: exception cancel of an unfinished order.
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorOrManager)]
    [HttpPut("{id}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateOrderStatus(string id, [FromBody] UpdateTestOrderStatusRequestDto request, CancellationToken ct)
    {
        var updated = await testOrderService.UpdateOrderStatusAsync(id, request, CurrentUserId(), User.IsInRole(RoleNames.Manager), ct);
        return Ok(new { success = true, message = $"Đơn {updated.OrderNumber}: {updated.Status}.", data = updated });
    }

    // WHY from the token only: GAP C3.4 / steering (c) — the acting user is never taken from the request body.
    private string CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
}
