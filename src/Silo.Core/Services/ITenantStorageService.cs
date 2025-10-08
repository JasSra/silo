namespace Silo.Core.Services;

/// <summary>
/// Tenant-aware storage service that partitions data by tenant
/// </summary>
public interface ITenantStorageService
{
    /// <summary>
    /// Get tenant-specific bucket name
    /// </summary>
    string GetTenantBucketName(Guid tenantId, string bucketType = "files");
    
    /// <summary>
    /// Upload file to tenant-specific bucket
    /// </summary>
    Task<string> UploadFileAsync(Guid tenantId, string fileName, Stream fileStream, string contentType, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Download file from tenant-specific bucket
    /// </summary>
    Task<Stream> DownloadFileAsync(Guid tenantId, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete file from tenant-specific bucket
    /// </summary>
    Task DeleteFileAsync(Guid tenantId, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if file exists in tenant-specific bucket
    /// </summary>
    Task<bool> FileExistsAsync(Guid tenantId, string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get file URL from tenant-specific bucket
    /// </summary>
    Task<string> GetFileUrlAsync(Guid tenantId, string filePath, TimeSpan? expiresIn = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Initialize buckets for a new tenant
    /// </summary>
    Task InitializeTenantBucketsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete all buckets for a tenant (deprovision)
    /// </summary>
    Task DeleteTenantBucketsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get storage usage for a tenant across all buckets
    /// </summary>
    Task<long> GetTenantStorageUsageAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for tenant bucket naming strategy
/// </summary>
public class TenantBucketConfiguration
{
    public string BucketPrefix { get; set; } = "tenant";
    public bool UseTenantIdInName { get; set; } = true;
    public string Separator { get; set; } = "-";
    
    public Dictionary<string, string> BucketTypes { get; set; } = new()
    {
        { "files", "files" },
        { "thumbnails", "thumbnails" },
        { "versions", "versions" },
        { "backups", "backups" }
    };
}
