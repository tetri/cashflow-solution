// Ponto de entrada do Daemon de Consolidacao (Consolidated Worker).
//
// Este host em segundo plano executa continuamente o consumidor de mensagens RabbitMQ,
// processando eventos de transacao de forma assincrona, idempotente e resiliente.
//
// Decisao arquitetural: utilizar Microsoft.Extensions.Hosting.Host (Generic Host) em vez de
// WebApplication.CreateBuilder permite executar um processo de longa duracao sem o overhead
// do pipeline HTTP, pois este servico nao expoe endpoints REST — apenas consome mensagens.

using CashFlow.Consolidated.Infrastructure;
using CashFlow.Consolidated.Worker.Consumers;
using RabbitMQ.Client;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// -----------------------------------------------------------------------------------------
// Configuracao de Telemetria e Logs
// -----------------------------------------------------------------------------------------
// Utilizamos apenas o provedor de console para simplicidade neste estagio.
// Em producao, este bloco seria substituido por OpenTelemetry com exportacao para
// Elasticsearch ou outro backend de observabilidade.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    // Inclui o Scopes nos logs para correlacao de traceId em mensagens processadas
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

// -----------------------------------------------------------------------------------------
// Registro da Infraestrutura do Consolidado (PostgreSQL e Redis)
// -----------------------------------------------------------------------------------------
builder.Services.AddConsolidatedInfrastructure(builder.Configuration);

// -----------------------------------------------------------------------------------------
// Registro da Conexao RabbitMQ como Singleton
// -----------------------------------------------------------------------------------------
// A IConnection do RabbitMQ e thread-safe e deve ser reutilizada por toda a vida do processo.
// Criar multiplas conexoes por operacao e um anti-padrao que satura o broker.
// Cada consumidor (IModel / canal) cria seu proprio canal a partir desta conexao compartilhada.
builder.Services.AddSingleton<IConnection>(sp =>
{
    var config = builder.Configuration;
    var rabbitHost = config["RabbitMQ:HostName"] ?? "localhost";
    var rabbitPort = int.Parse(config["RabbitMQ:Port"] ?? "5672");
    var rabbitUser = config["RabbitMQ:UserName"] ?? "guest";
    var rabbitPass = config["RabbitMQ:Password"] ?? "guest";

    var factory = new ConnectionFactory
    {
        HostName = rabbitHost,
        Port = rabbitPort,
        UserName = rabbitUser,
        Password = rabbitPass,
        // DispatchConsumersAsync = true e obrigatorio para uso de AsyncEventingBasicConsumer
        DispatchConsumersAsync = true,
        // AutomaticRecoveryEnabled permite que a conexao se reconecte automaticamente
        // em caso de queda temporaria do broker sem reiniciar o processo inteiro
        AutomaticRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
    };

    var logger = sp.GetRequiredService<ILogger<IConnection>>();
    logger.LogInformation("Estabelecendo conexao com RabbitMQ em {Host}:{Port}...", rabbitHost, rabbitPort);

    return factory.CreateConnection();
});

// -----------------------------------------------------------------------------------------
// Registro do Multiplexador Redis como Singleton
// -----------------------------------------------------------------------------------------
// IConnectionMultiplexer e projetado explicitamente para ser compartilhado (thread-safe).
// A configuracao abortConnect=false permite que a aplicacao inicie mesmo que o Redis
// esteja temporariamente indisponivel, com as operacoes falhando graciosamente via fallback.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis")
        ?? "localhost:6379,abortConnect=false";

    var logger = sp.GetRequiredService<ILogger<IConnectionMultiplexer>>();
    logger.LogInformation("Estabelecendo multiplexador de conexao com Redis em {ConnectionString}...", connectionString);

    return ConnectionMultiplexer.Connect(connectionString);
});

// -----------------------------------------------------------------------------------------
// Registro do Hosted Service Consumidor de Eventos
// -----------------------------------------------------------------------------------------
// AddHostedService registra o TransactionEventConsumer como um IHostedService que e
// iniciado automaticamente quando o host sobe e encerrado graciosamente no shutdown.
builder.Services.AddHostedService<TransactionEventConsumer>();

// -----------------------------------------------------------------------------------------
// Construcao e Execucao do Host
// -----------------------------------------------------------------------------------------
var host = builder.Build();

var hostLogger = host.Services.GetRequiredService<ILogger<Program>>();
hostLogger.LogInformation("Consolidated Worker iniciando. Aguardando mensagens do RabbitMQ...");

await host.RunAsync();
