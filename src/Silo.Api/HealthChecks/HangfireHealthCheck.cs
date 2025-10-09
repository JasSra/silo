using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Silo.Api.HealthChecks;

public class HangfireHealthCheck : IHealthCheck
{
    private readonly ILogger<HangfireHealthCheck> _logger;

    public HangfireHealthCheck(ILogger<HangfireHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var monitoringApi = JobStorage.Current.GetMonitoringApi();
            var stats = monitoringApi.GetStatistics();
            
            // Check if there are any servers processing jobs
            var servers = monitoringApi.Servers();
            var isHealthy = servers.Count > 0;
            
            var data = new Dictionary<string, object>
            {
                ["servers"] = servers.Count,
                ["enqueued"] = stats.Enqueued,
                ["scheduled"] = stats.Scheduled,
                ["processing"] = stats.Processing,
                ["succeeded"] = stats.Succeeded,
                ["failed"] = stats.Failed,
                ["deleted"] = stats.Deleted,
                ["recurring"] = stats.Recurring
            };

            if (isHealthy)
            {
                return Task.FromResult(HealthCheckResult.Healthy(
                    "Hangfire is running",
                    data));
            }
            else
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "No Hangfire servers are running",
                    null,
                    data));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hangfire health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Hangfire is not accessible",
                ex,
                new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                }));
        }
    }
}
