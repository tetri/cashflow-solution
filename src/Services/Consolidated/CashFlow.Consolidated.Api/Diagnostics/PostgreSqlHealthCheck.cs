using CashFlow.Consolidated.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CashFlow.Consolidated.Api.Diagnostics;

/// <summary>
/// Verificador de prontidão (Readiness) para conectividade com o banco de dados PostgreSQL (modo leitura).
/// </summary>
public class PostgreSqlHealthCheck : IHealthCheck
{
    private readonly ConsolidatedDbContext _dbContext;

    public PostgreSqlHealthCheck(ConsolidatedDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var podeConectar = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return podeConectar
                ? HealthCheckResult.Healthy("Conexao com banco relacional PostgreSQL (perfil leitor) ativa e responsiva.")
                : HealthCheckResult.Unhealthy("Nao foi possivel estabelecer conexao com o banco relacional PostgreSQL.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Falha ao verificar saude do PostgreSQL: {ex.Message}", ex);
        }
    }
}
