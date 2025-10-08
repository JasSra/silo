using Microsoft.AspNetCore.Authorization;

namespace Silo.Api.Authorization;

// Permission-based authorization requirements
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // Check if user has the required permission claim
        var permissionClaims = context.User.FindAll("permission");
        
        if (permissionClaims.Any(c => c.Value == requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

// Role-based authorization requirements
public class RoleRequirement : IAuthorizationRequirement
{
    public string[] Roles { get; }

    public RoleRequirement(params string[] roles)
    {
        Roles = roles;
    }
}

public class RoleHandler : AuthorizationHandler<RoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        if (requirement.Roles.Any(role => context.User.IsInRole(role)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

// Tenant isolation requirement
public class TenantRequirement : IAuthorizationRequirement
{
    public bool RequireTenant { get; }

    public TenantRequirement(bool requireTenant = true)
    {
        RequireTenant = requireTenant;
    }
}

public class TenantHandler : AuthorizationHandler<TenantRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantRequirement requirement)
    {
        var tenantIdClaim = context.User.FindFirst("tenant_id");

        if (requirement.RequireTenant)
        {
            if (tenantIdClaim != null && !string.IsNullOrWhiteSpace(tenantIdClaim.Value))
            {
                context.Succeed(requirement);
            }
        }
        else
        {
            // No tenant required, always succeed
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
