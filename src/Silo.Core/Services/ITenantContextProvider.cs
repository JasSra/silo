namespace Silo.Core.Services;

/// <summary>
/// Provides tenant context for the current request/operation
/// </summary>
public interface ITenantContextProvider
{
    Guid GetCurrentTenantId();
    bool TryGetCurrentTenantId(out Guid tenantId);
}
