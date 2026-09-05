using CashFlow.Transactions.Domain.Interfaces;
using CashFlow.Transactions.Infrastructure.Messaging;
using CashFlow.Transactions.Infrastructure.Persistence;
using CashFlow.Transactions.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace CashFlow.Transactions.Infrastructure;

/// <summary>
/// Modulo de extensao para injecao de dependencias da camada de Infraestrutura do servico de transacoes.
/// Centraliza a configuracao do Entity Framework Core (PostgreSQL), dos repositorios de dados
/// e da infraestrutura de mensageria assincrona com RabbitMQ e politicas de resiliencia Polly.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra os servicos de infraestrutura de persistencia relacional e mensageria no container de DI.
    /// </summary>
    /// <param name="services">Colecao de servicos da aplicacao.</param>
    /// <param name="configuration">Configuracoes da aplicacao para obtencao de strings de conexao.</param>
    /// <returns>A colecao de servicos configurada para encadeamento fluente.</returns>
    public static IServiceCollection AddTransactionsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Configuracao da persistencia relacional com PostgreSQL via Npgsql
        var connectionString = configuration.GetConnectionString("Database")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetConnectionString("PostgreSQL")
            ?? "Host=localhost;Port=5432;Database=cashflow_db;Username=postgres;Password=postgrespassword";

        services.AddDbContext<TransactionsDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // 2. Registro do repositorio de transacoes com ciclo de vida Scoped (por requisicao HTTP)
        services.AddScoped<ITransactionRepository, TransactionRepository>();

        // 3. Registro do publicador de eventos RabbitMQ resiliente com Polly v8
        services.AddSingleton<IConnection>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var host = config["RabbitMQ:HostName"] ?? "localhost";
            var port = int.TryParse(config["RabbitMQ:Port"], out var p) ? p : 5672;
            var user = config["RabbitMQ:UserName"] ?? "guest";
            var pass = config["RabbitMQ:Password"] ?? "guest";

            var factory = new ConnectionFactory
            {
                HostName = host,
                Port = port,
                UserName = user,
                Password = pass,
                DispatchConsumersAsync = true
            };

            return factory.CreateConnection();
        });

        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

        return services;
    }
}
