using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;

namespace Silo.Api.HealthChecks;

public class MinioHealthCheck : IHealthCheck
{
    private readonly IMinioClient _minioClient;
    private readonly ILogger<MinioHealthCheck> _logger;

    public MinioHealthCheck(IMinioClient minioClient, ILogger<MinioHealthCheck> logger)
    {
        _minioClient = minioClient;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to list buckets as a health check
            var buckets = await _minioClient.ListBucketsAsync(cancellationToken);
            
            return HealthCheckResult.Healthy(
                "MinIO is accessible",
                new Dictionary<string, object>
                {
                    ["buckets"] = buckets.Buckets.Count
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MinIO health check failed");
            return HealthCheckResult.Unhealthy(
                "MinIO is not accessible",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }
}
