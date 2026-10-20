using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing clinical laboratory test orders and physician requisitions (UC32 / Iteration 2).
/// Conforms to: Production Standards, Clean Architecture & AGENTS.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestOrdersController : ControllerBase
{
    private readonly ITestOrderService _testOrderService;

    public TestOrdersController(ITestOrderService testOrderService)
    {
        _testOrderService = testOrderService;
    }

    /// <summary>
    /// Retrieves standard clinical test catalog items with tube, pricing, and fasting specifications.
    /// </summary>
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog()
    {
        var items = await _testOrderService.GetAvailableCatalogAsync();
        return Ok(new
        {
            success = true,
            data = items
        });
    }

    /// <summary>
    /// Retrieves test orders filtered by patient ID, doctor ID, or status.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] string? patientId, [FromQuery] string? doctorId, [FromQuery] string? status)
    {
        var orders = await _testOrderService.GetOrdersAsync(patientId, doctorId, status);
        return Ok(new
        {
            success = true,
            total = ((List<TestOrderResponseDto>)orders).Count,
            data = orders
        });
    }

    /// <summary>
    /// Retrieves a test order by ID or order number.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrderById(string id)
    {
        var order = await _testOrderService.GetOrderByIdAsync(id);
        if (order == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Test order '{id}' not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = order
        });
    }

    /// <summary>
    /// Creates a new physician laboratory test order (UC32).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TestOrderResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateTestOrderRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Validation failed.",
                errors = ModelState
            });
        }

        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(currentUserId) && string.IsNullOrEmpty(request.DoctorId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "Vui lòng đăng nhập để tạo phiếu chỉ định xét nghiệm."
            });
        }

        try
        {
            var created = await _testOrderService.CreateTestOrderAsync(request, currentUserId);
            return CreatedAtAction(nameof(GetOrderById), new { id = created.OrderNumber }, new
            {
                success = true,
                message = $"Test order '{created.OrderNumber}' created successfully.",
                data = created
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
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new
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
    }

    /// <summary>
    /// Updates the status of an existing test order.
    /// </summary>
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateOrderStatus(string id, [FromBody] UpdateTestOrderStatusRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Validation failed.",
                errors = ModelState
            });
        }

        var updated = await _testOrderService.UpdateOrderStatusAsync(id, request);
        if (updated == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Test order '{id}' not found."
            });
        }

        return Ok(new
        {
            success = true,
            message = $"Test order status updated to '{updated.Status}'.",
            data = updated
        });
    }
}
