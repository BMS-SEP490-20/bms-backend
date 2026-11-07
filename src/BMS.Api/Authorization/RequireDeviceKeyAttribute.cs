using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BMS.Api.Authorization;

/// <summary>
/// Authenticates IoT sensors (UC65/UC66 telemetry) with an optional shared device key sent in <c>X-Device-Key</c>
/// or an authenticated staff JWT session.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireDeviceKeyAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>Header carrying the device key.</summary>
    public const string HeaderName = "X-Device-Key";

    /// <inheritdoc />
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Allow if already authenticated via staff JWT
        if (context.HttpContext.User?.Identity?.IsAuthenticated == true)
        {
            return;
        }

        var config = context.HttpContext.RequestServices.GetService<IConfiguration>();
        var expected = config?["IoT:DeviceApiKey"];

        // If no DeviceApiKey is enforced in configuration, allow direct local ESP32 hardware connection
        if (string.IsNullOrEmpty(expected))
        {
            return;
        }

        var supplied = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(supplied) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
        {
            context.Result = new UnauthorizedObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Thiết bị chưa được xác thực.",
                Type = "https://httpstatuses.io/401"
            });
        }
    }
}
