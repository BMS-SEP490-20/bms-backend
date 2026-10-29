using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System.Security.Claims;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Controller managing medical billing invoices and patient fee payments (UC43 / UC23).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/[controller]")]
[Produces("application/json")]
// WHY: UC54: Receptionist; Manager view only. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class InvoicesController(IReceiptService receiptService) : ControllerBase
{
    /// <summary>
    /// Retrieves paginated list of billing invoices with optional filters.
    /// </summary>
    [Authorize(Roles = RoleSets.ReceptionOrManager)]
    [HttpGet]
    public async Task<IActionResult> GetInvoices(
        [FromQuery] string? patientId,
        [FromQuery] InvoiceStatus? status,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var invoices = await receiptService.GetInvoicesAsync(patientId, status, fromDate, toDate, page, pageSize, ct);
        return Ok(new
        {
            success = true,
            total = invoices.Count,
            page,
            pageSize,
            data = invoices
        });
    }

    /// <summary>
    /// Retrieves invoice details by identifier or invoice code.
    /// </summary>
    [Authorize(Roles = RoleSets.ReceptionOrManager)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetInvoiceById(string id, CancellationToken ct = default)
    {
        var invoice = await receiptService.GetByIdAsync(id, ct);
        if (invoice == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Không tìm thấy hóa đơn với mã '{id}'."
            });
        }

        return Ok(new
        {
            success = true,
            data = invoice
        });
    }

    /// <summary>
    /// Creates a new billing invoice for patient consultation / lab testing.
    /// </summary>
    [Authorize(Roles = RoleNames.Receptionist)]
    [HttpPost]
    public async Task<IActionResult> CreateInvoice(
        [FromBody] CreateInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, errors = ModelState });
        }

        var created = await receiptService.CreateInvoiceAsync(request, ct);
        return CreatedAtAction(nameof(GetInvoiceById), new { id = created.Id }, new
        {
            success = true,
            message = "Tạo hóa đơn thành công.",
            data = created
        });
    }

    /// <summary>
    /// Records payment for an invoice (Cash, POS, VNPay, Transfer).
    /// </summary>
    [Authorize(Roles = RoleNames.Receptionist)]
    [HttpPost("{id}/pay")]
    public async Task<IActionResult> PayInvoice(
        string id,
        [FromBody] PayInvoiceRequest request,
        CancellationToken ct = default)
    {
        var cashierId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "CASHIER-SYS";
        var cashierName = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "Thu ngân BMS";

        try
        {
            var updated = await receiptService.PayInvoiceAsync(id, request, cashierId, cashierName, ct);
            return Ok(new
            {
                success = true,
                message = "Thanh toán hóa đơn thành công.",
                data = updated
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }
}
