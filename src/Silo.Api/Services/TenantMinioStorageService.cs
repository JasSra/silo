using Minio;
using Minio.DataModel.Args;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Silo.Core.Services;

namespace Silo.Api.Services;

/// <summary>
/// Tenant-aware MinIO storage service implementation
/// </summary>
public class TenantMinioStorageService : ITenantStorageService
{
    private readonly IMinioClient _minioClient;
    private readonly ILogger<TenantMinioStorageService> _logger;
    private readonly TenantBucketConfiguration _config;

    public TenantMinioStorageService(
        IMinioClient minioClient,
        ILogger<TenantMinioStorageService> logger,
        IOptions<TenantBucketConfiguration> config)
    {
        _minioClient = minioClient;
        _logger = logger;
        _config = config.Value;
    }

    public string GetTenantBucketName(Guid tenantId, string bucketType = "files")
    {
        if (!_config.BucketTypes.TryGetValue(bucketType, out var bucketSuffix))
        {
            bucketSuffix = bucketType;
        }

        // Pattern: tenant-{tenantId}-files (e.g., tenant-550e8400-e29b-41d4-a716-446655440000-files)
        return $"{_config.BucketPrefix}{_config.Separator}{tenantId:N}{_config.Separator}{bucketSuffix}";
    }

    public async Task<string> UploadFileAsync(
        Guid tenantId,
        string fileName,
        Stream fileStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var bucketName = GetTenantBucketName(tenantId, "files");
        
        // Ensure bucket exists
        await EnsureBucketExistsAsync(bucketName, cancellationToken);

        var objectName = $"{Guid.NewGuid():N}/{fileName}";

        try
        {
            await _minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType), cancellationToken);

            _logger.LogInformation("Uploaded file {FileName} to tenant bucket {BucketName} as {ObjectName}",
                fileName, bucketName, objectName);

            return objectName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {FileName} to tenant {TenantId}", fileName, tenantId);
            throw;
        }
    }

    public async Task<Stream> DownloadFileAsync(
        Guid tenantId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var bucketName = GetTenantBucketName(tenantId, "files");

        try
        {
            var memoryStream = new MemoryStream();
            await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(filePath)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream)), cancellationToken);

            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {FilePath} from tenant {TenantId}", filePath, tenantId);
            throw;
        }
    }

    public async Task DeleteFileAsync(
        Guid tenantId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var bucketName = GetTenantBucketName(tenantId, "files");

        try
        {
            await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(bucketName)
                .WithObject(filePath), cancellationToken);

            _logger.LogInformation("Deleted file {FilePath} from tenant {TenantId}", filePath, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file {FilePath} from tenant {TenantId}", filePath, tenantId);
            throw;
        }
    }

    public async Task<bool> FileExistsAsync(
        Guid tenantId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var bucketName = GetTenantBucketName(tenantId, "files");

        try
        {
            await _minioClient.StatObjectAsync(new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(filePath), cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetFileUrlAsync(
        Guid tenantId,
        string filePath,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        var bucketName = GetTenantBucketName(tenantId, "files");
        var expiry = (int)(expiresIn?.TotalSeconds ?? 3600); // Default 1 hour

        try
        {
            var url = await _minioClient.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(filePath)
                .WithExpiry(expiry));

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate URL for file {FilePath} in tenant {TenantId}", filePath, tenantId);
            throw;
        }
    }

    public async Task InitializeTenantBucketsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing buckets for tenant {TenantId}", tenantId);

        foreach (var bucketType in _config.BucketTypes.Keys)
        {
            var bucketName = GetTenantBucketName(tenantId, bucketType);
            await EnsureBucketExistsAsync(bucketName, cancellationToken);
        }

        _logger.LogInformation("Successfully initialized {Count} buckets for tenant {TenantId}",
            _config.BucketTypes.Count, tenantId);
    }

    public async Task DeleteTenantBucketsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Deleting all buckets for tenant {TenantId}", tenantId);

        foreach (var bucketType in _config.BucketTypes.Keys)
        {
            var bucketName = GetTenantBucketName(tenantId, bucketType);

            try
            {
                // First, delete all objects in the bucket
                var objects = _minioClient.ListObjectsEnumAsync(new ListObjectsArgs()
                    .WithBucket(bucketName)
                    .WithRecursive(true), cancellationToken);

                await foreach (var obj in objects)
                {
                    await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(obj.Key), cancellationToken);
                }

                // Then delete the bucket
                await _minioClient.RemoveBucketAsync(new RemoveBucketArgs()
                    .WithBucket(bucketName), cancellationToken);

                _logger.LogInformation("Deleted bucket {BucketName} for tenant {TenantId}", bucketName, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete bucket {BucketName} for tenant {TenantId}", bucketName, tenantId);
            }
        }
    }

    public async Task<long> GetTenantStorageUsageAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        long totalSize = 0;

        foreach (var bucketType in _config.BucketTypes.Keys)
        {
            var bucketName = GetTenantBucketName(tenantId, bucketType);

            try
            {
                var objects = _minioClient.ListObjectsEnumAsync(new ListObjectsArgs()
                    .WithBucket(bucketName)
                    .WithRecursive(true), cancellationToken);

                await foreach (var obj in objects)
                {
                    totalSize += (long)obj.Size;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate size for bucket {BucketName}", bucketName);
            }
        }

        return totalSize;
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken)
    {
        try
        {
            var exists = await _minioClient.BucketExistsAsync(new BucketExistsArgs()
                .WithBucket(bucketName), cancellationToken);

            if (!exists)
            {
                await _minioClient.MakeBucketAsync(new MakeBucketArgs()
                    .WithBucket(bucketName), cancellationToken);

                _logger.LogInformation("Created bucket {BucketName}", bucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure bucket {BucketName} exists", bucketName);
            throw;
        }
    }
}
