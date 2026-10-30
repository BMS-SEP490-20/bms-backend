using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Billing;
using BMS.Api.Authorization;
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
// WHY: UC55: payments only by Receptionist; Patient may pay own order online (VNPay only, server-computed amount, decision 15). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
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
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpGet("checkout/{invoiceId}")]
    public async Task<IActionResult> GetCheckoutSummary(string invoiceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            return BadRequest(new { success = false, message = "Invoice ID cannot be empty." });
        }

        if (!await PatientOwnsInvoiceAsync(invoiceId, ct)) return InvoiceNotFound();

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
    [Authorize(Roles = RoleNames.Receptionist)]
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
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpPost("process")]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequestDto request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (User.IsInRole(RoleNames.Patient))
        {
            // WHY (decision 15, TIEU-CHI R13): a patient may only pay their own invoice online via VNPay;
            // cash/POS stays with Lễ tân. The amount is always taken from the stored invoice by the service.
            if (!string.Equals(request.PaymentMethod?.Trim(), "VNPay", StringComparison.OrdinalIgnoreCase))
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Bệnh nhân chỉ được thanh toán trực tuyến qua VNPay." });
            if (!await PatientOwnsInvoiceAsync(request.InvoiceId, ct)) return InvoiceNotFound();
            request.PatientId = null; // never trust a client-supplied patient on the patient channel
        }

        try
        {
            var userId = User.AccountId();
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name;

            var result = await _paymentService.ProcessPaymentAsync(request, userId, userName, ct);

            return Ok(new
            {
                success = true,
                message = result.Message,
                data = result
            });
        }
        catch (KeyNotFoundException)
        {
            return InvoiceNotFound();
        }
        catch (ArgumentException)
        {
            // Fixed message: exception text may echo internal identifiers (no ex.Message leaks).
            return BadRequest(new { success = false, message = "Yêu cầu thanh toán không hợp lệ." });
        }
        // Unexpected errors are handled by GlobalExceptionHandler (no exception details leaked to the client).
    }

    /// <summary>Same 404 for "missing" and "not yours", so a patient cannot probe other invoices.</summary>
    private NotFoundObjectResult InvoiceNotFound()
        => NotFound(new { success = false, message = "Không tìm thấy hóa đơn." });

    /// <summary>Staff (already role-checked) pass; a Patient passes only for an invoice of their own patient record.</summary>
    private async Task<bool> PatientOwnsInvoiceAsync(string invoiceId, CancellationToken ct)
    {
        if (!User.IsInRole(RoleNames.Patient)) return true;
        var patientId = await _paymentService.GetInvoicePatientIdAsync(invoiceId, ct);
        return patientId is not null && await this.MayAccessPatientAsync(patientId, ct);
    }

    /// <summary>
    /// Retrieves all invoices and payment receipts for a specific patient.
    /// </summary>
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientInvoices(string patientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return BadRequest(new { success = false, message = "Patient ID cannot be empty." });
        }
        if (!await this.MayAccessPatientAsync(patientId, ct)) return Forbid();

        var list = await _paymentService.GetPatientInvoicesAsync(patientId, ct);
        return Ok(new
        {
            success = true,
            total = list.Count,
            data = list
        });
    }

    // ===================== Generate Receipt (I3-019) — payment records =====================
    // Đợt 2 ruling: counter payments and receipts (create, status, receipt) are Lễ tân only; Admin never touches
    // payments; Manager may only read the payment list/detail (matrix UC54 "QL xem").

    /// <summary>
    /// Creates a new payment transaction record.
    /// </summary>
    [Authorize(Roles = RoleNames.Receptionist)]
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
    [Authorize(Roles = RoleSets.ReceptionOrManager)]
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
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpGet("by-patient/{patientId}")]
    public async Task<IActionResult> GetByPatient(
        string patientId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!await this.MayAccessPatientAsync(patientId, ct)) return Forbid();
        var (list, totalCount) = await _paymentRecords.GetByPatientPagedAsync(patientId, page, pageSize, ct);
        return Ok(Paged(list, totalCount, page, pageSize));
    }

    /// <summary>
    /// Retrieves all payment transactions with pagination support.
    /// </summary>
    [Authorize(Roles = RoleSets.ReceptionOrManager)]
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
    [Authorize(Roles = RoleNames.Receptionist)]
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
    [Authorize(Roles = RoleNames.Receptionist)]
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
    [Authorize(Roles = RoleNames.Receptionist)]
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
