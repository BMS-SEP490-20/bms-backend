using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using BMS.Core.Constants;

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
        // A signed-in patient is not a telemetry device or an authorized operator.
        if (context.HttpContext.User?.Identity?.IsAuthenticated == true)
        {
            if (!RoleSets.AdminManagerLab.Split(',').Any(context.HttpContext.User.IsInRole))
                context.Result = new ForbidResult();
            return;
        }

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
