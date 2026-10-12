using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Catalog;

/// API controller for UC01 – View Service Catalog.
/// Public endpoint (AllowAnonymous) so patients can browse the catalog
/// before registering or logging in.
/// Role: All.
[ApiController]
[Route("api/v1/catalog")]
[Produces("application/json")]
public class ServiceCatalogController : ControllerBase
{
    private readonly ITestPackageService _service;
    private readonly ILogger<ServiceCatalogController> _logger;

    /// Initializes the controller.
    public ServiceCatalogController(
        ITestPackageService service,
        ILogger<ServiceCatalogController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// Retrieves the active laboratory service catalog.
    /// UC01 – View Service Catalog (Backend).
    /// Supports optional filtering by category or searching by name/code.
    /// This endpoint is publicly accessible (AllowAnonymous) so patients
    /// can browse services before signing up or logging in.
    /// <param name="category">Filter by service category (e.g. Hematology, Biochemistry). Empty = all.</param>
    /// <param name="search">Keyword to search in package name or code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ServiceCatalogResponseDto containing packages and categories.</returns>
    /// <response code="200">Catalog retrieved successfully.</response>
    /// <response code="500">Internal server error.</response>
    [HttpGet("services")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ServiceCatalogResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ServiceCatalogResponseDto>> GetServiceCatalog(
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "UC01 GetServiceCatalog called. Category={Category}, Search={Search}",
            category, search);

        var result = await _service.GetServiceCatalogAsync(category, search, cancellationToken);
        return Ok(result);
    }

    /// Retrieves a single service package by its Id.
    /// <param name="id">Package Id (e.g. PKG-CBC-01).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>TestPackageDto or 404 if not found.</returns>
    /// <response code="200">Package found.</response>
    /// <response code="404">Package not found or no longer offered.</response>
    [HttpGet("services/{id}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TestPackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TestPackageDto>> GetServiceById(
        string id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("UC01 GetServiceById called. Id={Id}", id);

        var result = await _service.GetByIdAsync(id, cancellationToken);
        if (result is null)
            return NotFound(new { message = $"Service package with Id '{id}' was not found." });

        return Ok(result);
    }
}