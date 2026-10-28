using Microsoft.AspNetCore.Mvc;
using payment_service.Models;
using payment_service.Repositories;

namespace payment_service.Controllers;

/// <summary>
/// Controller managing patient payments, financial transactions, and receipt generation (UC43 / UC23).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class PaymentsController(IPaymentRepository repo) : ControllerBase
{
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

        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            PatientId = request.PatientId,
            Amount = request.Amount,
            Notes = request.Notes,
            Status = "Pending",
            PaidAt = request.PaidAt,
            CreatedAt = request.CreatedAt != default ? request.CreatedAt : DateTime.UtcNow,
            TransactionCode = request.TransactionCode,
            TxnRef = request.TxnRef,
        };

        var created = await repo.CreateAsync(payment);

        return CreatedAtAction(nameof(GetById), new { id = created.PaymentId }, new
        {
            success = true,
            message = "Payment created successfully.",
            data = created
        });
    }

    /// <summary>
    /// Retrieves payment history and details by patient identifier with pagination support.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (page <= 0 || pageSize <= 0)
        {
            return BadRequest(new { success = false, message = "Page and pageSize must be greater than 0." });
        }

        var list = await repo.GetByPatientIdPagedAsync(id, page, pageSize);
        var totalCount = await repo.CountByPatientIdAsync(id);

        return Ok(new
        {
            success = true,
            total = totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            data = list
        });
    }

    /// <summary>
    /// Retrieves paginated list of payments for a specific patient.
    /// </summary>
    [HttpGet("by-patient/{patientId}")]
    public async Task<IActionResult> GetByPatient(
        Guid patientId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (page <= 0 || pageSize <= 0)
        {
            return BadRequest(new { success = false, message = "Page and pageSize must be greater than 0." });
        }

        var list = await repo.GetByPatientIdPagedAsync(patientId, page, pageSize);
        var totalCount = await repo.CountByPatientIdAsync(patientId);

        return Ok(new
        {
            success = true,
            total = totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            data = list
        });
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
        if (page <= 0 || pageSize <= 0)
        {
            return BadRequest(new { success = false, message = "Page and pageSize must be greater than 0." });
        }

        var list = await repo.GetAllPagedAsync(page, pageSize);
        var totalCount = await repo.CountAllAsync();

        return Ok(new
        {
            success = true,
            total = totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            data = list
        });
    }

    /// <summary>
    /// Updates the status and reference details of an existing payment transaction.
    /// </summary>
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdatePaymentStatusRequest request,
        CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(new { success = false, message = "Status is required." });
        }

        var payment = await repo.GetByIdAsync(id);
        if (payment == null)
        {
            return NotFound(new { success = false, message = $"Payment with id '{id}' was not found." });
        }

        payment.Status = request.Status;

        if (!string.IsNullOrWhiteSpace(request.TransactionCode))
        {
            payment.TransactionCode = request.TransactionCode;
        }

        if (!string.IsNullOrWhiteSpace(request.TxnRef))
        {
            payment.TxnRef = request.TxnRef;
        }

        await repo.UpdateAsync(payment);

        return Ok(new
        {
            success = true,
            message = "Payment status updated successfully.",
            data = payment
        });
    }

    /// <summary>
    /// Automatically calculates service fees and generates an itemized payment receipt or electronic invoice for requested test packages.
    /// Intended for Receptionist role upon patient billing and checkout.
    /// </summary>
    [HttpPost("receipt")]
    public async Task<IActionResult> GenerateReceipt([FromBody] GenerateReceiptRequest request, CancellationToken ct = default)
    {
        if (request == null)
        {
            return BadRequest(new { success = false, message = "Receipt request payload cannot be null." });
        }

        if (request.PatientId == Guid.Empty)
        {
            return BadRequest(new { success = false, message = "PatientId is required." });
        }

        // Calculate item amounts and subtotal
        var items = new List<ReceiptItemDto>();
        decimal subTotal = 0;

        if (request.Items != null && request.Items.Any())
        {
            foreach (var item in request.Items)
            {
                var quantity = item.Quantity > 0 ? item.Quantity : 1;
                var itemAmount = quantity * item.UnitPrice;
                subTotal += itemAmount;

                items.Add(new ReceiptItemDto
                {
                    PackageId = item.PackageId,
                    PackageName = item.PackageName,
                    Quantity = quantity,
                    UnitPrice = item.UnitPrice,
                    Amount = itemAmount
                });
            }
        }

        var taxPercent = request.TaxPercent.GetValueOrDefault(0);
        var taxAmount = Math.Round(subTotal * taxPercent / 100, 2);
        var discountAmount = Math.Round(request.DiscountAmount.GetValueOrDefault(0), 2);
        var totalAmount = Math.Max(0, subTotal + taxAmount - discountAmount);

        var randomSuffix = Guid.NewGuid().ToString("N")[..6].ToUpper();
        var receiptNumber = $"REC-{DateTime.UtcNow:yyyyMMdd}-{randomSuffix}";

        // Persist payment transaction record
        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            PatientId = request.PatientId,
            Amount = totalAmount,
            Notes = $"Receipt {receiptNumber} processed by {request.ReceptionistName ?? "Receptionist"} for Appointment {request.AppointmentId ?? "N/A"}",
            Status = "Success",
            PaidAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            TransactionCode = receiptNumber,
            TxnRef = receiptNumber
        };

        await repo.CreateAsync(payment);

        var receipt = new ReceiptDto
        {
            ReceiptNumber = receiptNumber,
            AppointmentId = request.AppointmentId,
            PatientId = request.PatientId,
            PatientName = request.PatientName,
            PatientPhone = request.PatientPhone,
            ReceptionistId = request.ReceptionistId,
            ReceptionistName = request.ReceptionistName,
            PaymentMethod = request.PaymentMethod ?? "Cash",
            Status = "Paid",
            ReceiptDate = DateTime.UtcNow,
            Items = items,
            SubTotal = subTotal,
            TaxPercent = taxPercent,
            TaxAmount = taxAmount,
            DiscountAmount = discountAmount,
            TotalAmount = totalAmount,
            Notes = request.Notes
        };

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
        if (string.IsNullOrWhiteSpace(receiptNumber))
        {
            return BadRequest(new { success = false, message = "Receipt number is required." });
        }

        var payment = await repo.GetByTxnRefAsync(receiptNumber);
        if (payment == null)
        {
            // Fallback: check all for matching transactionCode
            var all = await repo.GetAllAsync();
            payment = all.FirstOrDefault(p => string.Equals(p.TransactionCode, receiptNumber, StringComparison.OrdinalIgnoreCase));
        }

        if (payment == null)
        {
            return NotFound(new { success = false, message = $"Receipt with number '{receiptNumber}' was not found." });
        }

        return Ok(new
        {
            success = true,
            data = payment
        });
    }
}