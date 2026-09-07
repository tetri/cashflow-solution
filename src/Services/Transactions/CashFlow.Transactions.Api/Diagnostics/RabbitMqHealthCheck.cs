using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace CashFlow.Transactions.Api.Diagnostics;

/// <summary>
/// Verificador de prontidão (Readiness) para conectividade com o broker de mensageria RabbitMQ.
/// </summary>
public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IConnection _connection;

    public RabbitMqHealthCheck(IConnection connection)
    {
        _connection = connection;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_connection.IsOpen)
            {
                return Task.FromResult(HealthCheckResult.Healthy("Conexao com o broker RabbitMQ estabelecida e ativa."));
            }

            return Task.FromResult(HealthCheckResult.Unhealthy("Conexao com o broker RabbitMQ encontra-se fechada."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Falha ao verificar conexao com RabbitMQ: {ex.Message}", ex));
        }
    }
}
