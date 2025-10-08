using Microsoft.EntityFrameworkCore;
using Silo.Core.Data;
using Silo.Core.Services;

namespace Silo.Api.Services;

/// <summary>
/// Service for enforcing tenant quotas
/// </summary>
public interface IQuotaService
{
    Task<bool> CheckStorageQuotaAsync(Guid tenantId, long additionalBytes, CancellationToken cancellationToken = default);
    Task<bool> CheckUserQuotaAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> CheckApiKeyQuotaAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task UpdateStorageUsageAsync(Guid tenantId, long bytes, CancellationToken cancellationToken = default);
    Task<QuotaStatus> GetQuotaStatusAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public class QuotaService : IQuotaService
{
    private readonly SiloDbContext _context;
    private readonly ITenantStorageService _tenantStorage;
    private readonly ILogger<QuotaService> _logger;

    public QuotaService(
        SiloDbContext context,
        ITenantStorageService tenantStorage,
        ILogger<QuotaService> logger)
    {
        _context = context;
        _tenantStorage = tenantStorage;
        _logger = logger;
    }

    public async Task<bool> CheckStorageQuotaAsync(
        Guid tenantId,
        long additionalBytes,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);

        if (tenant == null)
        {
            _logger.LogWarning("Tenant {TenantId} not found for quota check", tenantId);
            return false;
        }

        // Unlimited quota (0 means unlimited)
        if (tenant.StorageQuotaBytes == 0)
        {
            return true;
        }

        var currentUsage = await _tenantStorage.GetTenantStorageUsageAsync(tenantId, cancellationToken);
        var projectedUsage = currentUsage + additionalBytes;

        if (projectedUsage > tenant.StorageQuotaBytes)
        {
            _logger.LogWarning(
                "Storage quota exceeded for tenant {TenantId}: {CurrentUsage} + {AdditionalBytes} > {Quota}",
                tenantId, currentUsage, additionalBytes, tenant.StorageQuotaBytes);
            return false;
        }

        return true;
    }

    public async Task<bool> CheckUserQuotaAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);

        if (tenant == null)
        {
            return false;
        }

        // Unlimited quota
        if (tenant.MaxUsers == 0)
        {
            return true;
        }

        var currentUserCount = await _context.Users
            .CountAsync(u => u.TenantId == tenantId && u.IsActive, cancellationToken);

        if (currentUserCount >= tenant.MaxUsers)
        {
            _logger.LogWarning(
                "User quota exceeded for tenant {TenantId}: {CurrentUsers} >= {MaxUsers}",
                tenantId, currentUserCount, tenant.MaxUsers);
            return false;
        }

        return true;
    }

    public async Task<bool> CheckApiKeyQuotaAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);

        if (tenant == null)
        {
            return false;
        }

        // Unlimited quota
        if (tenant.MaxApiKeys == 0)
        {
            return true;
        }

        var currentKeyCount = await _context.TenantApiKeys
            .CountAsync(k => k.TenantId == tenantId && k.IsActive && k.RevokedAt == null, cancellationToken);

        if (currentKeyCount >= tenant.MaxApiKeys)
        {
            _logger.LogWarning(
                "API key quota exceeded for tenant {TenantId}: {CurrentKeys} >= {MaxKeys}",
                tenantId, currentKeyCount, tenant.MaxApiKeys);
            return false;
        }

        return true;
    }

    public async Task UpdateStorageUsageAsync(
        Guid tenantId,
        long bytes,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);

        if (tenant == null)
        {
            return;
        }

        tenant.StorageUsedBytes += bytes;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<QuotaStatus> GetQuotaStatusAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);

        if (tenant == null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} not found");
        }

        var storageUsage = await _tenantStorage.GetTenantStorageUsageAsync(tenantId, cancellationToken);
        var userCount = await _context.Users.CountAsync(u => u.TenantId == tenantId && u.IsActive, cancellationToken);
        var apiKeyCount = await _context.TenantApiKeys
            .CountAsync(k => k.TenantId == tenantId && k.IsActive && k.RevokedAt == null, cancellationToken);

        return new QuotaStatus
        {
            TenantId = tenantId,
            TenantName = tenant.Name,
            Storage = new ResourceQuota
            {
                Used = storageUsage,
                Limit = tenant.StorageQuotaBytes,
                PercentUsed = tenant.StorageQuotaBytes > 0 
                    ? (double)storageUsage / tenant.StorageQuotaBytes * 100 
                    : 0,
                IsExceeded = tenant.StorageQuotaBytes > 0 && storageUsage >= tenant.StorageQuotaBytes
            },
            Users = new ResourceQuota
            {
                Used = userCount,
                Limit = tenant.MaxUsers,
                PercentUsed = tenant.MaxUsers > 0 
                    ? (double)userCount / tenant.MaxUsers * 100 
                    : 0,
                IsExceeded = tenant.MaxUsers > 0 && userCount >= tenant.MaxUsers
            },
            ApiKeys = new ResourceQuota
            {
                Used = apiKeyCount,
                Limit = tenant.MaxApiKeys,
                PercentUsed = tenant.MaxApiKeys > 0 
                    ? (double)apiKeyCount / tenant.MaxApiKeys * 100 
                    : 0,
                IsExceeded = tenant.MaxApiKeys > 0 && apiKeyCount >= tenant.MaxApiKeys
            }
        };
    }
}

public class QuotaStatus
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public ResourceQuota Storage { get; set; } = new();
    public ResourceQuota Users { get; set; } = new();
    public ResourceQuota ApiKeys { get; set; } = new();
}

public class ResourceQuota
{
    public long Used { get; set; }
    public long Limit { get; set; }
    public double PercentUsed { get; set; }
    public bool IsExceeded { get; set; }
}
