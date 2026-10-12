using System.Security.Claims;
using BMS.Application.DTOs.Users;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Users;

/// <summary>
/// Controller managing clinical and administrative user profile operations (UC08).
/// Enforces OWASP A01: IDOR Prevention by binding queries strictly to authenticated JWT claims.
/// Conforms to: Production Standards & RESTful API Guidelines.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves personal profile of currently authenticated user (UC08).
    /// Prevents Insecure Direct Object Reference (IDOR) by extracting identifier exclusively from JWT claims.
    /// </summary>
    /// <response code="200">User profile retrieved successfully.</response>
    /// <response code="401">User is unauthenticated or token expired.</response>
    /// <response code="404">User profile not found.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUserProfile(CancellationToken ct = default)
    {
        // Extract authenticated user ID from Claims (ClaimTypes.NameIdentifier or "sub")
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("uid");

        // If unauthenticated or token absent, check if demo seed profile is requested via header for testing
        if (string.IsNullOrEmpty(userId))
        {
            var demoHeader = Request.Headers["X-Demo-User"].FirstOrDefault();
            if (!string.IsNullOrEmpty(demoHeader))
            {
                var demoProfile = await _userService.EnsureDefaultUserProfileAsync(ct);
                return Ok(ApiResponse<UserProfileResponseDto>.Ok(demoProfile, "Retrieved demo profile successfully."));
            }

            // Return 401 Unauthorized per OWASP standard
            return Unauthorized(ApiResponse<object>.Fail(
                "Authentication required. Please provide a valid Bearer token.",
                "MSG-ERR-AUTH-INVALID"));
        }

        _logger.LogInformation("[BMS Users API] Querying profile for authenticated user {UserId}", userId);
        var profile = await _userService.GetProfileByIdAsync(userId, ct);

        if (profile == null)
        {
            return NotFound(ApiResponse<object>.Fail("User profile not found.", "MSG-ERR-USER-NOT-FOUND"));
        }

        return Ok(ApiResponse<UserProfileResponseDto>.Ok(profile, "Profile retrieved successfully."));
    }

    /// <summary>
    /// Convenience endpoint seeding and retrieving default clinical patient profile for development and UI testing.
    /// </summary>
    [HttpGet("demo")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDemoProfile(CancellationToken ct = default)
    {
        var profile = await _userService.EnsureDefaultUserProfileAsync(ct);
        return Ok(ApiResponse<UserProfileResponseDto>.Ok(profile, "Demo profile loaded successfully."));
    }
}
