using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Controller handling patient billing fee checkout and payment processing (UC55 / Process Payment).
/// Supports VNPay, MoMo, Banking QR, and Pay-at-reception options.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    /// <summary>
    /// Retrieves checkout summary and order breakdown for a specific invoice (UC55).
    /// </summary>
    [HttpGet("checkout/{invoiceId}")]
    public async Task<IActionResult> GetCheckoutSummary(string invoiceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            return BadRequest(new { success = false, message = "Invoice ID cannot be empty." });
        }

        var summary = await _paymentService.GetCheckoutSummaryAsync(invoiceId, ct);
        if (summary == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Invoice '{invoiceId}' not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = summary
        });
    }

    /// <summary>
    /// Validates and applies a promotional discount code to an invoice (UC55).
    /// </summary>
    [HttpPost("apply-discount")]
    public async Task<IActionResult> ApplyDiscount([FromBody] ApplyDiscountRequestDto request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _paymentService.ApplyDiscountAsync(request, ct);
        return Ok(new
        {
            success = result.IsValid,
            data = result
        });
    }

    /// <summary>
    /// Processes patient payment transaction (VNPay, MoMo, Banking QR, or Reception) (UC55).
    /// </summary>
    [HttpPost("process")]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequestDto request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name;

            var result = await _paymentService.ProcessPaymentAsync(request, userId, userName, ct);

            return Ok(new
            {
                success = true,
                message = result.Message,
                data = result
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while processing payment: " + ex.Message
            });
        }
    }

    /// <summary>
    /// Retrieves all invoices and payment receipts for a specific patient.
    /// </summary>
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientInvoices(string patientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return BadRequest(new { success = false, message = "Patient ID cannot be empty." });
        }

        var list = await _paymentService.GetPatientInvoicesAsync(patientId, ct);
        return Ok(new
        {
            success = true,
            total = list.Count,
            data = list
        });
    }
}
