using BMS.Api.Authorization;
using BMS.Application.DTOs;
using BMS.Application.DTOs.Common;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Catalog;

/// <summary>
/// API controller for UC01 – View Service Catalog &amp; Search Test Packages (Web &amp; Mobile).
/// Publicly accessible (AllowAnonymous) for patients and guests; staff with MANAGE_SERVICE_PACKAGE
/// can also view/filter inactive test packages.
/// </summary>
[ApiController]
[Route("api/v1/catalog")]
[Route("api/catalog")]
[Route("api/test-packages")]
[Route("api/v1/test-packages")]
[Produces("application/json")]
public class ServiceCatalogController : ControllerBase
{
    private readonly ITestPackageService _service;
    private readonly IAuthorizationService _authorization;
    private readonly ILogger<ServiceCatalogController> _logger;

    public ServiceCatalogController(
        ITestPackageService service,
        IAuthorizationService authorization,
        ILogger<ServiceCatalogController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Searches and filters test packages with keyword matching, category, sample tube, price range, sorting, and pagination.
    /// Accessible via GET /api/test-packages, GET /api/test-packages/search, GET /api/v1/catalog/services/search.
    /// </summary>
    [HttpGet]
    [HttpGet("search")]
    [HttpGet("services/search")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<TestPackageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<TestPackageDto>>> SearchTestPackages(
        [FromQuery] TestPackageSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var includeInactive = await CanManagePackagesAsync();
        _logger.LogInformation(
            "SearchTestPackages called. Keyword={Keyword}, Category={Category}, SampleTube={SampleTube}, Page={Page}",
            request.EffectiveKeyword, request.Category, request.SampleTube, request.Page);

        var result = await _service.SearchTestPackagesAsync(request, includeInactive, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the active laboratory service catalog (categories + packages).
    /// </summary>
    [HttpGet("services")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ServiceCatalogResponseDto), StatusCodes.Status200OK)]
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

    /// <summary>
    /// Retrieves a single test package by its Id or PackageCode (e.g. PKG-CBC-01 or CBC-01).
    /// </summary>
    [HttpGet("{id}")]
    [HttpGet("services/{id}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TestPackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TestPackageDto>> GetServiceById(
        string id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("UC01 GetServiceById called. Id={Id}", id);

        var includeInactive = await CanManagePackagesAsync();
        var result = await _service.GetByIdAsync(id, includeInactive, cancellationToken);
        if (result is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = $"Không tìm thấy gói xét nghiệm '{id}'.",
                Extensions = { ["code"] = "TEST_PACKAGE_NOT_FOUND", ["traceId"] = HttpContext.TraceIdentifier }
            });
        }

        return Ok(result);
    }

    private async Task<bool> CanManagePackagesAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
            return false;

        return (await _authorization.AuthorizeAsync(
                User, (object?)null, $"{PermissionPolicy.Prefix}{Permissions.ManageServicePackage}")).Succeeded;
    }
}