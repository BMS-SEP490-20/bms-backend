using BMS.Api.Authorization;
using BMS.Application.DTOs;
using BMS.Application.DTOs.Common;
using BMS.Application.Interfaces;
using BMS.Core.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Catalog;

/// <summary>
/// API controller for UC01 &amp; UC20 – View Service Catalog &amp; Search Test Packages (Web &amp; Mobile).
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
    /// Searches and filters test packages with keyword matching, category, sample tube, price range, sorting, and pagination (UC20).
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
        try
        {
            var includeInactive = await CanManagePackagesAsync();
            _logger.LogInformation(
                "UC20 SearchTestPackages called. Keyword={Keyword}, Category={Category}, SampleTube={SampleTube}, Page={Page}",
                request.EffectiveKeyword, request.Category, request.SampleTube, request.Page);

            var result = await _service.SearchTestPackagesAsync(request, includeInactive, cancellationToken);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                success = false,
                message = ex.Message,
                title = ex.Message,
                code = ex.Code,
                status = ex.StatusCode
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UC20 SearchTestPackages failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Không thể tìm kiếm danh mục gói xét nghiệm lúc này. Vui lòng thử lại sau.",
                title = "Không thể tìm kiếm danh mục gói xét nghiệm lúc này. Vui lòng thử lại sau.",
                code = "SEARCH_TEST_PACKAGES_ERROR",
                status = StatusCodes.Status500InternalServerError
            });
        }
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
        try
        {
            _logger.LogInformation(
                "UC01 GetServiceCatalog called. Category={Category}, Search={Search}",
                category, search);

            var result = await _service.GetServiceCatalogAsync(category, search, cancellationToken);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                success = false,
                message = ex.Message,
                title = ex.Message,
                code = ex.Code,
                status = ex.StatusCode
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UC01 GetServiceCatalog failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Không thể tải danh mục gói xét nghiệm lúc này. Vui lòng thử lại sau.",
                title = "Không thể tải danh mục gói xét nghiệm lúc này. Vui lòng thử lại sau.",
                code = "GET_SERVICE_CATALOG_ERROR",
                status = StatusCodes.Status500InternalServerError
            });
        }
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
        try
        {
            _logger.LogInformation("UC01/UC20 GetServiceById called. Id={Id}", id);

            var includeInactive = await CanManagePackagesAsync();
            var result = await _service.GetByIdAsync(id, includeInactive, cancellationToken);
            if (result is null)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"Không tìm thấy gói xét nghiệm '{id}'.",
                    title = $"Không tìm thấy gói xét nghiệm '{id}'.",
                    code = "TEST_PACKAGE_NOT_FOUND",
                    status = StatusCodes.Status404NotFound,
                    traceId = HttpContext.TraceIdentifier
                });
            }

            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                success = false,
                message = ex.Message,
                title = ex.Message,
                code = ex.Code,
                status = ex.StatusCode
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UC01/UC20 GetServiceById failed for Id={Id}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Không thể tải chi tiết gói xét nghiệm lúc này. Vui lòng thử lại sau.",
                title = "Không thể tải chi tiết gói xét nghiệm lúc này. Vui lòng thử lại sau.",
                code = "GET_TEST_PACKAGE_ERROR",
                status = StatusCodes.Status500InternalServerError
            });
        }
    }

    private async Task<bool> CanManagePackagesAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
            return false;

        return (await _authorization.AuthorizeAsync(
                User, (object?)null, $"{PermissionPolicy.Prefix}{Permissions.ManageServicePackage}")).Succeeded;
    }
}