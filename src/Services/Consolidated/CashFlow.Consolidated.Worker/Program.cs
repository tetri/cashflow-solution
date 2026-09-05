// Ponto de entrada do Daemon de Segundo Plano (Consolidated Worker)
// Responsavel pelo processamento continuo e idempotente de mensagens vindas do RabbitMQ.

var builder = Host.CreateApplicationBuilder(args);

// Configuracao de logs e infraestrutura base de background hosting
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var host = builder.Build();
host.Run();
