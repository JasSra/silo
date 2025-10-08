using System.ComponentModel.DataAnnotations;

namespace Silo.Core.Models;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [StringLength(50)]
    public string Slug { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Subscription/Plan info
    [StringLength(50)]
    public string SubscriptionTier { get; set; } = "Free";
    public DateTime? SubscriptionExpiresAt { get; set; }
    
    // Resource quotas
    public long StorageQuotaBytes { get; set; } = 1_073_741_824; // 1GB default
    public long StorageUsedBytes { get; set; } = 0;
    public int MaxUsers { get; set; } = 5;
    public int MaxApiKeys { get; set; } = 2;
    
    // Settings
    public string? CustomDomain { get; set; }
    public string? LogoUrl { get; set; }
    public string Settings { get; set; } = "{}"; // JSON settings
    
    // Navigation properties
    public virtual ICollection<User> Users { get; set; } = new List<User>();
    public virtual ICollection<TenantApiKey> ApiKeys { get; set; } = new List<TenantApiKey>();
}

public class TenantApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public string KeyHash { get; set; } = string.Empty;
    
    [StringLength(10)]
    public string KeyPrefix { get; set; } = string.Empty; // First 8 chars for display
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    
    // Permissions/scopes for this API key
    public string Scopes { get; set; } = ""; // Comma-separated permissions
    
    public virtual Tenant Tenant { get; set; } = null!;
}

// DTOs
public record CreateTenantRequest(
    [Required] string Name,
    [Required] string Slug,
    string? Description = null,
    string SubscriptionTier = "Free");

public record UpdateTenantRequest(
    string? Name = null,
    string? Description = null,
    bool? IsActive = null,
    string? SubscriptionTier = null);

public record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    bool IsActive,
    string SubscriptionTier,
    long StorageQuotaBytes,
    long StorageUsedBytes,
    int MaxUsers,
    DateTime CreatedAt);

public record CreateApiKeyRequest(
    [Required] string Name,
    DateTime? ExpiresAt = null,
    string[]? Scopes = null);

public record ApiKeyDto(
    Guid Id,
    string Name,
    string Key, // Only returned on creation
    string KeyPrefix,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    string[] Scopes);
