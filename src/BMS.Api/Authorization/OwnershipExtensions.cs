using System.Security.Claims;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Authorization;

/// <summary>Helpers for "own data only" rules (matrix "(own)" cells, TIEU-CHI R14).</summary>
public static class OwnershipExtensions
{
    /// <summary>Account ID from the token (<c>sub</c>, falling back to NameIdentifier). Never read from the request.</summary>
    public static string? AccountId(this ClaimsPrincipal user)
        => user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Returns true when the caller may read <paramref name="patientId"/>: staff roles pass (their role was already
    /// checked by <c>[Authorize]</c>); a Patient passes only for their own record.
    /// </summary>
    /// <remarks>
    /// The ownership service is resolved lazily so that hosts which never admit a Patient (e.g. UC check hosts)
    /// need not register it. If a Patient arrives and the service is missing, access is denied (fail closed).
    /// </remarks>
    public static async Task<bool> MayAccessPatientAsync(this ControllerBase controller, string patientId, CancellationToken ct)
    {
        var user = controller.User;
        if (!user.IsInRole(RoleNames.Patient)) return true;
        var accountId = user.AccountId();
        var ownership = controller.HttpContext.RequestServices.GetService<IPatientOwnership>();
        return accountId is not null && ownership is not null && await ownership.IsOwnPatientAsync(accountId, patientId, ct);
    }

    /// <summary>True when <paramref name="userId"/> is the caller's own account (inbox/device-token endpoints).</summary>
    public static bool IsSelf(this ControllerBase controller, string userId)
        => string.Equals(controller.User.AccountId(), userId, StringComparison.Ordinal);
}
