using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BMS.Api.Authorization;

/// <summary>
/// Phân quyền theo permission lấy từ claim <c>permission</c> của JWT. Dùng: <c>[HasPermission("CREATE_INVOICE")]</c>.
/// Chuỗi nhiều permission cách nhau dấu phẩy = cần ít nhất 1. Request chưa đăng nhập luôn bị 401.
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
        // WHY (TIEU-CHI R6): never succeed for anonymous callers. The old handler let unauthenticated
        // "demo" requests and every staff/Manager/Admin role through, which made [HasPermission] a no-op.
        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var granted = context.User.FindAll("permission").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (granted.Contains(PermissionPolicy.All))
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
            // RequireAuthenticatedUser => anonymous callers get 401 (challenge), not 403.
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..]))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}

