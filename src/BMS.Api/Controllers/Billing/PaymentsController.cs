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
/// Also hosts the payment record and receipt endpoints of Generate Receipt (I3-019), merged here from the
/// duplicate payment_service PaymentsController so that <c>api/Payments</c> is served by a single controller.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentRecordService _paymentRecords;

    public PaymentsController(IPaymentService paymentService, IPaymentRecordService paymentRecords)
    {
        _paymentService = paymentService;
        _paymentRecords = paymentRecords;
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
        // Unexpected errors are handled by GlobalExceptionHandler (no exception details leaked to the client).
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

    // ===================== Generate Receipt (I3-019) — payment records =====================
    // TODO(Đợt 2): add [Authorize] with Receptionist/Admin roles; currently anonymous like the original.

    /// <summary>
    /// Creates a new payment transaction record.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, errors = ModelState });
        }

        var created = await _paymentRecords.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.PaymentId }, new
        {
            success = true,
            message = "Payment created successfully.",
            data = created
        });
    }

    /// <summary>
    /// Retrieves a payment transaction by its id.
    /// (The legacy version returned the payments of a patient here; use <c>by-patient/{patientId}</c> for that.)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var payment = await _paymentRecords.GetByIdAsync(id, ct);
        return payment is null
            ? NotFound(new { success = false, message = $"Payment with id '{id}' was not found." })
            : Ok(new { success = true, data = payment });
    }

    /// <summary>
    /// Retrieves paginated list of payments for a specific patient.
    /// </summary>
    [HttpGet("by-patient/{patientId}")]
    public async Task<IActionResult> GetByPatient(
        string patientId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var (list, totalCount) = await _paymentRecords.GetByPatientPagedAsync(patientId, page, pageSize, ct);
        return Ok(Paged(list, totalCount, page, pageSize));
    }

    /// <summary>
    /// Retrieves all payment transactions with pagination support.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var (list, totalCount) = await _paymentRecords.GetAllPagedAsync(page, pageSize, ct);
        return Ok(Paged(list, totalCount, page, pageSize));
    }

    /// <summary>
    /// Updates the status and reference details of an existing payment transaction.
    /// </summary>
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdatePaymentStatusRequest request,
        CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(new { success = false, message = "Status is required." });
        }

        var payment = await _paymentRecords.UpdateStatusAsync(id, request, ct);
        return payment is null
            ? NotFound(new { success = false, message = $"Payment with id '{id}' was not found." })
            : Ok(new { success = true, message = "Payment status updated successfully.", data = payment });
    }

    /// <summary>
    /// Automatically calculates service fees and generates an itemized payment receipt or electronic invoice for requested test packages.
    /// </summary>
    [HttpPost("receipt")]
    public async Task<IActionResult> GenerateReceipt([FromBody] GenerateReceiptRequest request, CancellationToken ct = default)
    {
        if (request == null)
        {
            return BadRequest(new { success = false, message = "Receipt request payload cannot be null." });
        }

        var receipt = await _paymentRecords.GenerateReceiptAsync(request, ct);
        return Ok(new
        {
            success = true,
            message = "Payment receipt generated successfully.",
            data = receipt
        });
    }

    /// <summary>
    /// Retrieves receipt payment details by receipt number or transaction code.
    /// </summary>
    [HttpGet("receipt/{receiptNumber}")]
    public async Task<IActionResult> GetReceiptByNumber(string receiptNumber, CancellationToken ct = default)
    {
        var payment = await _paymentRecords.GetByReceiptNumberAsync(receiptNumber, ct);
        return payment is null
            ? NotFound(new { success = false, message = $"Receipt with number '{receiptNumber}' was not found." })
            : Ok(new { success = true, data = payment });
    }

    private static object Paged<T>(IReadOnlyList<T> list, long totalCount, int page, int pageSize) => new
    {
        success = true,
        total = totalCount,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
        data = list
    };
}
