using CashFlow.Transactions.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CashFlow.Transactions.Api.Diagnostics;

/// <summary>
/// Verificador de prontidão (Readiness) para conectividade com o banco de dados PostgreSQL.
/// </summary>
public class PostgreSqlHealthCheck : IHealthCheck
{
    private readonly TransactionsDbContext _dbContext;

    public PostgreSqlHealthCheck(TransactionsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var podeConectar = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return podeConectar
                ? HealthCheckResult.Healthy("Conexao com banco relacional PostgreSQL ativa e responsiva.")
                : HealthCheckResult.Unhealthy("Nao foi possivel estabelecer conexao com o banco relacional PostgreSQL.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Falha ao verificar saude do PostgreSQL: {ex.Message}", ex);
        }
    }
}
