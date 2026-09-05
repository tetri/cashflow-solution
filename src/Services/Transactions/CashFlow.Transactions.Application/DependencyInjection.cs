using CashFlow.Transactions.Application.Commands.CreateTransaction;
using Microsoft.Extensions.DependencyInjection;

namespace CashFlow.Transactions.Application;

/// <summary>
/// Modulo de extensao para registro de servicos da camada de Aplicacao no container de injecao de dependencias.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adiciona os manipuladores de comando (CommandHandlers) e servicos de aplicacao ao container de servicos.
    /// </summary>
    /// <param name="services">Colecao de servicos da aplicacao.</param>
    /// <returns>A colecao de servicos configurada.</returns>
    public static IServiceCollection AddTransactionsApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateTransactionCommandHandler>();
        return services;
    }
}
