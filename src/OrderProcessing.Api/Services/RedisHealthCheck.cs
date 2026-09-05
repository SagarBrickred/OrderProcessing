using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace OrderProcessing.Api.Services;

/// <summary>
/// Kubernetes calls GET /health to decide whether this pod is ready to receive
/// traffic (and whether to restart it). If Redis is unreachable, we report
/// "Unhealthy" here rather than letting requests fail one at a time.
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis responded in {latency.TotalMilliseconds}ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis ping failed", ex);
        }
    }
}
