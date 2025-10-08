using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silo.Api.Services;
using Silo.Core.Data;
using Silo.Core.Models;
using Silo.Core.Services;

namespace Silo.Api.Controllers;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class TenantsController : ControllerBase
{
    private readonly SiloDbContext _context;
    private readonly ITenantStorageService _tenantStorage;
    private readonly TenantOpenSearchIndexingService _tenantSearch;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(
        SiloDbContext context,
        ITenantStorageService tenantStorage,
        TenantOpenSearchIndexingService tenantSearch,
        ILogger<TenantsController> logger)
    {
        _context = context;
        _tenantStorage = tenantStorage;
        _tenantSearch = tenantSearch;
        _logger = logger;
    }

    /// <summary>
    /// Get all tenants
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TenantDto>>> GetTenants(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Tenants.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        var tenants = await query
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var tenantDtos = tenants.Select(t => new TenantDto(
            t.Id,
            t.Name,
            t.Slug,
            t.Description,
            t.IsActive,
            t.SubscriptionTier,
            t.StorageQuotaBytes,
            t.StorageUsedBytes,
            t.MaxUsers,
            t.CreatedAt
        ));

        return Ok(tenantDtos);
    }

    /// <summary>
    /// Get tenant by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantDto>> GetTenant(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { id }, cancellationToken);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        return Ok(new TenantDto(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.Description,
            tenant.IsActive,
            tenant.SubscriptionTier,
            tenant.StorageQuotaBytes,
            tenant.StorageUsedBytes,
            tenant.MaxUsers,
            tenant.CreatedAt
        ));
    }

    /// <summary>
    /// Create a new tenant with full provisioning
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TenantDto>> CreateTenant(
        [FromBody] CreateTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if slug is unique
            if (await _context.Tenants.AnyAsync(t => t.Slug == request.Slug, cancellationToken))
            {
                return BadRequest(new { error = "Tenant slug already exists" });
            }

            var tenant = new Tenant
            {
                Name = request.Name,
                Slug = request.Slug,
                Description = request.Description,
                SubscriptionTier = request.SubscriptionTier,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync(cancellationToken);

            // Provision infrastructure
            _logger.LogInformation("Provisioning infrastructure for tenant {TenantId}", tenant.Id);

            // Create MinIO buckets
            await _tenantStorage.InitializeTenantBucketsAsync(tenant.Id, cancellationToken);

            // Create OpenSearch indexes
            await _tenantSearch.InitializeTenantIndexesAsync(tenant.Id, cancellationToken);

            _logger.LogInformation("Successfully provisioned tenant {TenantId}", tenant.Id);

            return CreatedAtAction(
                nameof(GetTenant),
                new { id = tenant.Id },
                new TenantDto(
                    tenant.Id,
                    tenant.Name,
                    tenant.Slug,
                    tenant.Description,
                    tenant.IsActive,
                    tenant.SubscriptionTier,
                    tenant.StorageQuotaBytes,
                    tenant.StorageUsedBytes,
                    tenant.MaxUsers,
                    tenant.CreatedAt
                )
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create tenant");
            return StatusCode(500, new { error = "Failed to create tenant" });
        }
    }

    /// <summary>
    /// Update tenant configuration
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TenantDto>> UpdateTenant(
        Guid id,
        [FromBody] UpdateTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { id }, cancellationToken);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        if (request.Name != null) tenant.Name = request.Name;
        if (request.Description != null) tenant.Description = request.Description;
        if (request.IsActive.HasValue) tenant.IsActive = request.IsActive.Value;
        if (request.SubscriptionTier != null) tenant.SubscriptionTier = request.SubscriptionTier;

        tenant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new TenantDto(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.Description,
            tenant.IsActive,
            tenant.SubscriptionTier,
            tenant.StorageQuotaBytes,
            tenant.StorageUsedBytes,
            tenant.MaxUsers,
            tenant.CreatedAt
        ));
    }

    /// <summary>
    /// Update tenant quotas
    /// </summary>
    [HttpPost("{id:guid}/quotas")]
    public async Task<IActionResult> UpdateQuotas(
        Guid id,
        [FromBody] UpdateQuotasRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { id }, cancellationToken);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        if (request.StorageQuotaBytes.HasValue)
            tenant.StorageQuotaBytes = request.StorageQuotaBytes.Value;

        if (request.MaxUsers.HasValue)
            tenant.MaxUsers = request.MaxUsers.Value;

        if (request.MaxApiKeys.HasValue)
            tenant.MaxApiKeys = request.MaxApiKeys.Value;

        tenant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            tenantId = tenant.Id,
            storageQuotaBytes = tenant.StorageQuotaBytes,
            maxUsers = tenant.MaxUsers,
            maxApiKeys = tenant.MaxApiKeys
        });
    }

    /// <summary>
    /// Get tenant usage statistics
    /// </summary>
    [HttpGet("{id:guid}/usage")]
    public async Task<ActionResult> GetUsage(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { id }, cancellationToken);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        // Calculate actual storage usage
        var storageUsage = await _tenantStorage.GetTenantStorageUsageAsync(id, cancellationToken);

        // Update tenant record
        tenant.StorageUsedBytes = storageUsage;
        await _context.SaveChangesAsync(cancellationToken);

        var userCount = await _context.Users.CountAsync(u => u.TenantId == id, cancellationToken);
        var apiKeyCount = await _context.TenantApiKeys.CountAsync(k => k.TenantId == id && k.IsActive, cancellationToken);

        return Ok(new
        {
            tenantId = id,
            tenantName = tenant.Name,
            storage = new
            {
                used = storageUsage,
                quota = tenant.StorageQuotaBytes,
                percentUsed = tenant.StorageQuotaBytes > 0 
                    ? (double)storageUsage / tenant.StorageQuotaBytes * 100 
                    : 0
            },
            users = new
            {
                count = userCount,
                quota = tenant.MaxUsers,
                percentUsed = tenant.MaxUsers > 0 
                    ? (double)userCount / tenant.MaxUsers * 100 
                    : 0
            },
            apiKeys = new
            {
                count = apiKeyCount,
                quota = tenant.MaxApiKeys,
                percentUsed = tenant.MaxApiKeys > 0 
                    ? (double)apiKeyCount / tenant.MaxApiKeys * 100 
                    : 0
            }
        });
    }

    /// <summary>
    /// Deactivate tenant (soft delete)
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateTenant(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { id }, cancellationToken);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        // Soft delete - deactivate tenant
        tenant.IsActive = false;
        tenant.UpdatedAt = DateTime.UtcNow;

        // Deactivate all users
        var users = await _context.Users.Where(u => u.TenantId == id).ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            user.IsActive = false;
        }

        // Revoke all API keys
        var apiKeys = await _context.TenantApiKeys.Where(k => k.TenantId == id).ToListAsync(cancellationToken);
        foreach (var key in apiKeys)
        {
            key.RevokedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Deactivated tenant {TenantId}", id);

        return Ok(new { message = "Tenant deactivated successfully" });
    }

    /// <summary>
    /// Permanently delete tenant and all data (dangerous!)
    /// </summary>
    [HttpDelete("{id:guid}/purge")]
    public async Task<IActionResult> PurgeTenant(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _context.Tenants.FindAsync(new object[] { id }, cancellationToken);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        _logger.LogWarning("Purging tenant {TenantId} - this will delete all data!", id);

        try
        {
            // Delete MinIO buckets
            await _tenantStorage.DeleteTenantBucketsAsync(id, cancellationToken);

            // Delete OpenSearch indexes
            await _tenantSearch.DeleteTenantIndexesAsync(id, cancellationToken);

            // Delete database records
            _context.Tenants.Remove(tenant);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Successfully purged tenant {TenantId}", id);

            return Ok(new { message = "Tenant purged successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge tenant {TenantId}", id);
            return StatusCode(500, new { error = "Failed to purge tenant" });
        }
    }
}

public record UpdateQuotasRequest(
    long? StorageQuotaBytes = null,
    int? MaxUsers = null,
    int? MaxApiKeys = null);
