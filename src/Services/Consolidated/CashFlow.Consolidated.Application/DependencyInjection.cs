using CashFlow.Consolidated.Application.Queries.GetDailyConsolidated;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Consolidated.Application;

/// <summary>
/// Ponto de extensao central para injecao de dependencias da camada de Aplicacao do servico de Consolidado.
/// Registra os manipuladores de consulta (QueryHandlers) e componentes de logica de negocio.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adiciona servicos da camada de aplicacao do consolidado no conteiner de injecao de dependencia.
    /// </summary>
    /// <param name="services">Colecao de descritores de servico da aplicacao.</param>
    /// <returns>A mesma instancia para encadeamento fluente.</returns>
    public static IServiceCollection AddConsolidatedApplication(this IServiceCollection services)
    {
        // Registro do manipulador de consulta CQRS com ciclo de vida Scoped
        services.AddScoped<GetDailyConsolidatedQueryHandler>();

        return services;
    }
}
