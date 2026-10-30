using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BMS.Api.Authorization;

/// <summary>
/// Authenticates IoT sensors (UC65/UC66 telemetry) with a shared device key sent in <c>X-Device-Key</c>.
/// </summary>
/// <remarks>
/// WHY (TIEU-CHI C13, Senior Dev choice): sensors are not users and cannot hold a JWT, but the telemetry
/// endpoint must not be open to anyone (matrix row UC65). The key is read from configuration
/// <c>IoT:DeviceApiKey</c> (set via env <c>IoT__DeviceApiKey</c>); when it is not configured the endpoint
/// fails closed with 401. Comparison is constant-time. mTLS can replace this later without touching callers' routes.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireDeviceKeyAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>Header carrying the device key.</summary>
    public const string HeaderName = "X-Device-Key";

    /// <inheritdoc />
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetService<IConfiguration>();
        var expected = config?["IoT:DeviceApiKey"];
        var supplied = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied) ||
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
