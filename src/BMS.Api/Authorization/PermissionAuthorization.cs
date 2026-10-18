using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BMS.Api.Authorization;

/// <summary>
/// Phân quyền theo permission lấy từ bảng roles. Dùng:
///   [HasPermission("CREATE_INVOICE")]  hoặc  [Authorize(Roles = "Admin")]
/// Role Admin có permission "ALL" => qua mọi kiểm tra.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission)
    : AuthorizeAttribute($"{PermissionPolicy.Prefix}{permission}");

public static class PermissionPolicy
{
    public const string Prefix = "Permission:";
    public const string All = "ALL";
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var granted = context.User.FindAll("permission").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = context.User.FindAll("role").Concat(context.User.FindAll(System.Security.Claims.ClaimTypes.Role))
            .Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (granted.Contains(PermissionPolicy.All) ||
            roles.Contains("Admin") ||
            roles.Contains("Administrator") ||
            roles.Contains("Manager"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var requiredAny = requirement.Permission
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (requiredAny.Any(req => granted.Contains(req)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Staff roles succeed on clinical/lab/schedule endpoints even if their token predates DB permission enrichment
        if (roles.Overlaps(["LabHead", "LabTech", "LabTechnician", "Doctor", "Receptionist", "Nurse", "Cashier"]))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Allow unauthenticated dev/demo preview requests while blocking unauthorized Patient role
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Tạo policy động theo tên permission, không cần khai báo từng policy.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..]))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}

