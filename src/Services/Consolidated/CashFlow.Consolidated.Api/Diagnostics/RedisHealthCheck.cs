using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CashFlow.Consolidated.Api.Diagnostics;

/// <summary>
/// Verificador de prontidão (Readiness) para conectividade com o cache distribuído Redis.
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_redis.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Multiplexador do cache Redis nao esta conectado.");
            }

            var latencia = await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Conexao com cache Redis ativa e responsiva (latencia: {latencia.TotalMilliseconds:F2}ms).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Falha ao verificar saude do cache Redis: {ex.Message}", ex);
        }
    }
}
