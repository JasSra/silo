using Silo.Core.Services;

namespace Silo.Api.Services;

/// <summary>
/// HTTP context-based tenant provider (gets tenant from JWT claims or API key)
/// </summary>
public class HttpTenantContextProvider : ITenantContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantContextProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetCurrentTenantId()
    {
        if (TryGetCurrentTenantId(out var tenantId))
        {
            return tenantId;
        }

        throw new InvalidOperationException("No tenant context available. User must be authenticated with a valid tenant.");
    }

    public bool TryGetCurrentTenantId(out Guid tenantId)
    {
        tenantId = Guid.Empty;

        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
        {
            return false;
        }

        var tenantClaim = user.FindFirst("tenant_id");
        if (tenantClaim == null)
        {
            return false;
        }

        return Guid.TryParse(tenantClaim.Value, out tenantId);
    }
}
