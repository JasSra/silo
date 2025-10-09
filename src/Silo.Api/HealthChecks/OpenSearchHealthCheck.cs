using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenSearch.Client;

namespace Silo.Api.HealthChecks;

public class OpenSearchHealthCheck : IHealthCheck
{
    private readonly OpenSearchClient _client;
    private readonly ILogger<OpenSearchHealthCheck> _logger;

    public OpenSearchHealthCheck(OpenSearchClient client, ILogger<OpenSearchHealthCheck> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pingResult = await _client.PingAsync(ct: cancellationToken);
            
            if (pingResult.IsValid)
            {
                var clusterHealth = await _client.Cluster.HealthAsync(ct: cancellationToken);
                
                return HealthCheckResult.Healthy(
                    "OpenSearch is accessible",
                    new Dictionary<string, object>
                    {
                        ["status"] = clusterHealth.Status.ToString(),
                        ["nodes"] = clusterHealth.NumberOfNodes,
                        ["dataNodes"] = clusterHealth.NumberOfDataNodes
                    });
            }
            else
            {
                return HealthCheckResult.Degraded(
                    "OpenSearch ping failed",
                    null,
                    new Dictionary<string, object>
                    {
                        ["error"] = pingResult.DebugInformation
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSearch health check failed");
            return HealthCheckResult.Unhealthy(
                "OpenSearch is not accessible",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
        }
    }
}
