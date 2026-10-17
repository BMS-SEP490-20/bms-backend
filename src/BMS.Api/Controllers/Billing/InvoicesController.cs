using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Controller managing medical billing invoices and patient fee payments (UC23 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    /// <summary>
    /// Retrieves paginated list of billing invoices.
    /// </summary>
    [HttpGet]
    public IActionResult GetInvoices([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Medical Invoices API endpoint is scheduled for Iteration 2."
        });
    }

    /// <summary>
    /// Retrieves invoice details by identifier.
    /// </summary>
    [HttpGet("{id}")]
    public IActionResult GetInvoiceById(string id)
    {
        return Ok(new
        {
            success = true,
            id,
            status = "Scheduled",
            milestone = "Iteration 2"
        });
    }
}
