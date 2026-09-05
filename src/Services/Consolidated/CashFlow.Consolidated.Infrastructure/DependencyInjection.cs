using CashFlow.Consolidated.Domain.Interfaces;
using CashFlow.Consolidated.Infrastructure.Caching;
using CashFlow.Consolidated.Infrastructure.Persistence;
using CashFlow.Consolidated.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Consolidated.Infrastructure;

/// <summary>
/// Ponto central de registro de todas as dependencias da camada de infraestrutura
/// do servico de Consolidado Diario (banco de dados PostgreSQL, cache Redis e repositorios).
///
/// Decisao arquitetural: centralizar o registro de dependencias em um metodo de extensao
/// de IServiceCollection segue o padrao "Infrastructure Registration" da Clean Architecture,
/// mantendo o projeto de apresentacao (API ou Worker) desacoplado dos detalhes concretos
/// das implementacoes de persistencia e cache.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra todos os servicos de infraestrutura do consolidado no contêiner de DI.
    /// </summary>
    /// <param name="services">Colecao de descritores de servico do host.</param>
    /// <param name="configuration">Fonte de configuracoes da aplicacao (appsettings / variaveis de ambiente).</param>
    /// <returns>A mesma colecao para encadeamento fluente de chamadas.</returns>
    public static IServiceCollection AddConsolidatedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Registro do DbContext com conexao ao PostgreSQL via Npgsql.
        // O escopo Scoped e correto para DbContext: uma instancia por requisicao HTTP (API) ou
        // por ciclo de processamento de mensagem (Worker), evitando state compartilhado indesejado.
        services.AddDbContext<ConsolidatedDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Database")
                ?? throw new InvalidOperationException(
                    "A string de conexao 'Database' nao foi encontrada nas configuracoes. " +
                    "Verifique a variavel de ambiente 'ConnectionStrings__Database' ou o appsettings.json.");

            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                // Timeout de comando individual para evitar bloqueio indefinido em consultas lentas
                npgsqlOptions.CommandTimeout(30);

                // O suporte nativo a DateOnly e TimeOnly no PostgreSQL e provido pelo Npgsql >= 6.0.
                // Nao e necessario chamar extensoes adicionais; o provider ja realiza o mapeamento correto.
            });

            // Em producao, logs de queries SQL devem ser suprimidos para evitar vazamento de dados sensiveis.
            // Para depuracao local, habilitar via appsettings: "Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command": "Information"
        });

        // Registro do servico de cache Redis como Singleton.
        // O IConnectionMultiplexer e thread-safe e projetado para reutilizacao — nao deve ser Scoped.
        // O registro da conexao e realizado no Program.cs de cada host (Worker / API).
        services.AddScoped<IConsolidatedCacheService, RedisConsolidatedCacheService>();

        // Registro do repositorio de saldo diario com escopo de requisicao/mensagem
        services.AddScoped<IDailyConsolidatedRepository, DailyConsolidatedRepository>();

        return services;
    }
}
