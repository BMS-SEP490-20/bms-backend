using System.Security.Claims;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Controller managing POS Receipt Printing and verification (UC43 / BR-131).
/// Enforces 'Paid' invoice invariant, atomic print counter increments, and anti-fraud duplicate reprint headers.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class ReceiptsController(IReceiptService receiptService) : ControllerBase
{
    /// <summary>
    /// Generates thermal K80 print data for an invoice and increments the print counter.
    /// Invariant: Only invoices with Status == Paid can be printed.
    /// </summary>
    [HttpGet("{invoiceId}/print-data")]
    public async Task<IActionResult> GetPrintData(string invoiceId, CancellationToken ct = default)
    {
        var cashierId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? User.FindFirst("sub")?.Value 
            ?? "CASHIER-SYS";
        var cashierName = User.FindFirst(ClaimTypes.Name)?.Value 
            ?? User.Identity?.Name 
            ?? "Thu ngân BMS";

        try
        {
            var printData = await receiptService.GeneratePrintDataAsync(invoiceId, cashierId, cashierName, ct);
            return Ok(new
            {
                success = true,
                message = "Dữ liệu in biên lai được tạo thành công.",
                data = printData
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Verifies authenticity of a printed receipt via ReceiptNumber or QR code.
    /// </summary>
    [HttpGet("verify/{receiptNumber}")]
    public async Task<IActionResult> VerifyReceipt(string receiptNumber, CancellationToken ct = default)
    {
        var result = await receiptService.VerifyReceiptAsync(receiptNumber, ct);
        if (!result.IsValid && result.Status == "NotFound")
        {
            return NotFound(new
            {
                success = false,
                message = $"Không tìm thấy biên lai với mã '{receiptNumber}' trên hệ thống.",
                data = result
            });
        }

        return Ok(new
        {
            success = true,
            data = result
        });
    }
}
