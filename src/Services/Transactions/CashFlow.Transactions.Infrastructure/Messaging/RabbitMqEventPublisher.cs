using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CashFlow.Shared.Domain.Events;
using CashFlow.Transactions.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace CashFlow.Transactions.Infrastructure.Messaging;

/// <summary>
/// Publicador de eventos de dominio integrado ao RabbitMQ com politicas avancadas
/// de resiliencia, tolerancia a falhas e concorrencia implementadas via Polly v8.
/// Esta classe garante a publicacao de mensagens duraveis com confirmacao de broker,
/// aplicando Circuit Breaker, Retry exponencial com jitter, Timeout e sincronizacao de canal AMQP.
/// </summary>
public class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly object _publishLock = new();
    private const string ExchangeName = "cashflow.events";

    /// <summary>
    /// Inicializa a infraestrutura de publicacao declarando a Topic Exchange duravel
    /// e construindo o pipeline composto de resiliencia Polly v8.
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

        // Construtor de predicado de excecoes de rede transientes suportadas pelas politicas
        var predicateBuilder = new PredicateBuilder()
            .Handle<SocketException>()
            .Handle<BrokerUnreachableException>()
            .Handle<RabbitMQClientException>()
            .Handle<TimeoutException>()
            .Handle<TimeoutRejectedException>()
            .Handle<IOException>();

        // Construcao do pipeline de resiliencia composto Polly v8:
        // 1. Circuit Breaker: interrompe o fluxo quando 50% das requisicoes falham em 10s (minimo 5 chamadas),
        // mantendo o circuito aberto por 15s para recuperacao do broker e protecao contra sobrecarga.
        // 2. Retry Exponencial com Jitter: realiza 3 tentativas com delay base de 200ms e dispersao aleatoria
        // para evitar sincronizacao em manada (Thundering Herd).
        // 3. Timeout: impoe limite maximo estrito de 3 segundos por execucao.
        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(15),
                ShouldHandle = predicateBuilder,
                OnOpened = args =>
                {
                    _logger.LogError(
                        args.Outcome.Exception,
                        "Disjuntor de publicacao RabbitMQ aberto por {BreakDuration} segundos devido a falhas recorrentes.",
                        args.BreakDuration.TotalSeconds
                    );
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("Disjuntor de publicacao RabbitMQ restabelecido com sucesso.");
                    return ValueTask.CompletedTask;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = predicateBuilder,
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
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(3)
            })
            .Build();
    }

    /// <summary>
    /// Publica uma mensagem de evento serializada em JSON UTF-8 com entrega persistente no RabbitMQ.
    /// Utiliza bloqueio de exclusao mutua (lock) para garantir a thread-safety do IModel AMQP.
    /// </summary>
    /// <typeparam name="T">Tipo da entidade ou contrato do evento.</typeparam>
    /// <param name="event">Objeto de evento contendo as informacoes da operacao.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    public async Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = typeof(T).Name;
        var routingKey = $"transaction.{eventType.ToLowerInvariant()}";

        var payload = JsonSerializer.Serialize(@event);
        var body = Encoding.UTF8.GetBytes(payload);

        // Configuracao dos metadados de entrega confiavel AMQP
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.DeliveryMode = 2; // Persistencia forcada em disco pelo broker RabbitMQ
        properties.ContentType = "application/json";
        properties.ContentEncoding = "utf-8";
        properties.Type = eventType;
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Vincula o CorrelationId ao EventId do IDomainEvent para rastreabilidade ponta a ponta
        if (@event is IDomainEvent domainEvent)
        {
            properties.CorrelationId = domainEvent.EventId.ToString();
            properties.MessageId = domainEvent.EventId.ToString();
        }
        else
        {
            var correlationId = Guid.NewGuid().ToString();
            properties.CorrelationId = correlationId;
            properties.MessageId = correlationId;
        }

        // Execucao da publicacao protegida pelo pipeline composto de resiliencia Polly
        await _resiliencePipeline.ExecuteAsync(async state =>
        {
            // Thread-safety: o canal IModel do RabbitMQ.Client nao e thread-safe para publicacoes concorrentes
            lock (_publishLock)
            {
                _channel.BasicPublish(
                    exchange: ExchangeName,
                    routingKey: routingKey,
                    basicProperties: properties,
                    body: body
                );
            }

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
