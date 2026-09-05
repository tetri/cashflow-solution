using System.Text;
using System.Text.Json;
using CashFlow.Transactions.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;

namespace CashFlow.Transactions.Infrastructure.Messaging;

/// <summary>
/// Publicador de eventos de dominio integrado ao RabbitMQ com politicas avancadas
/// de resiliencia e tolerancia a falhas implementadas via Polly v8.
/// Esta classe garante a publicacao de mensagens duraveis com confirmacao de broker,
/// mitigando picos de latencia e quedas transientes na comunicacao AMQP.
/// </summary>
public class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;
    private const string ExchangeName = "cashflow.events";

    /// <summary>
    /// Inicializa a infraestrutura de publicacao declarando a Topic Exchange duravel
    /// e construindo o pipeline de resiliencia com Retry Exponencial, Jitter e Timeout.
    /// </summary>
    /// <param name="connection">Conexao gerenciada com o broker RabbitMQ.</param>
    /// <param name="logger">Mecanismo de telemetria e diagnostico.</param>
    public RabbitMqEventPublisher(
        IConnection connection,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connection = connection;
        _logger = logger;
        _channel = _connection.CreateModel();

        // Declaracao da exchange de topico duravel para distribuicao de eventos de transacao
        _channel.ExchangeDeclare(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false
        );

        // Construcao do pipeline de resiliencia Polly v8:
        // O Retry exponencial evita a saturacao do broker durante recuperacoes de rede.
        // O Jitter (aleatoriedade no delay) impede o efeito de sincronizacao de rajadas (Thundering Herd).
        // O Timeout estrito de 3 segundos protege as threads do servidor contra bloqueios indefinidos.
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Tentativa {AttemptNumber} de publicar evento no RabbitMQ falhou. Aplicando retentativa com backoff.",
                        args.AttemptNumber
                    );
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(TimeSpan.FromSeconds(3))
            .Build();
    }

    /// <summary>
    /// Publica uma mensagem de evento serializada em JSON UTF-8 com entrega persistente no RabbitMQ.
    /// </summary>
    /// <typeparam name="T">Tipo da entidade ou contrato do evento.</typeparam>
    /// <param name="event">Objeto de evento contendo as informacoes da operacao.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    public async Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : class
    {
        var eventType = typeof(T).Name;
        var routingKey = $"transaction.{eventType.ToLowerInvariant()}";

        var payload = JsonSerializer.Serialize(@event);
        var body = Encoding.UTF8.GetBytes(payload);

        // Configuracao de cabecalhos de entrega confiavel:
        // DeliveryMode = 2 forca a persistencia da mensagem em disco pelo RabbitMQ.
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.Type = eventType;
        properties.MessageId = Guid.NewGuid().ToString();
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Execucao da publicacao protegida pelo pipeline de resiliencia Polly
        await _resiliencePipeline.ExecuteAsync(async state =>
        {
            _channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: routingKey,
                basicProperties: properties,
                body: body
            );
            _logger.LogInformation("Evento {EventType} publicado com sucesso para routingKey {RoutingKey}", eventType, routingKey);
            await Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>
    /// Libera os recursos nao gerenciados da conexao e canal AMQP.
    /// </summary>
    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }
}
