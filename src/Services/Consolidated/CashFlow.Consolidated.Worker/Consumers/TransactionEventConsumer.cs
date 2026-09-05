using System.Text;
using System.Text.Json;
using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Interfaces;
using CashFlow.Shared.Domain.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CashFlow.Consolidated.Worker.Consumers;

/// <summary>
/// Servico em segundo plano (BackgroundService / Daemon) responsavel pelo consumo assincrono
/// e confiavel dos eventos de transacao postados no RabbitMQ.
/// Implementa deduplicacao idempotente, atualizacao atomica de saldo no PostgreSQL,
/// sincronizacao imediata no Redis (Write-Through) e roteamento de mensagens venenosas para DLQ.
/// Utiliza IServiceScopeFactory para resolver dependencias Scoped (como o DbContext e Repositorios)
/// de forma segura a partir de um servico Singleton (BackgroundService).
/// </summary>
public class TransactionEventConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TransactionEventConsumer> _logger;

    private const string QueueName = "cashflow.consolidated.transactions";
    private const string ExchangeName = "cashflow.events";
    private const string DeadLetterExchangeName = "cashflow.events.dlx";
    private const string DeadLetterQueueName = "cashflow.consolidated.transactions.dlq";

    /// <summary>
    /// Inicializa a topologia AMQP: declara a Dead Letter Exchange (Direct),
    /// a fila DLQ duravel, a fila principal com argumentos x-dead-letter-* e configura o prefetch.
    /// </summary>
    public TransactionEventConsumer(
        IConnection connection,
        IServiceScopeFactory scopeFactory,
        ILogger<TransactionEventConsumer> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;

        _channel = _connection.CreateModel();

        // 1. Configuracao da Dead Letter Exchange e Queue para poison messages
        _channel.ExchangeDeclare(DeadLetterExchangeName, ExchangeType.Direct, durable: true);
        _channel.QueueDeclare(DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(DeadLetterQueueName, DeadLetterExchangeName, routingKey: "transactions.dlq");

        // 2. Configuracao da Fila Principal atrelada a DLX sob rejeicao (BasicNack requeue: false)
        var args = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", DeadLetterExchangeName },
            { "x-dead-letter-routing-key", "transactions.dlq" }
        };

        _channel.ExchangeDeclare(ExchangeName, ExchangeType.Topic, durable: true);
        _channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: args);
        _channel.QueueBind(QueueName, ExchangeName, routingKey: "transaction.*");

        // 3. Controle de vazao (QoS / Backpressure): limita em 20 mensagens nao confirmadas
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 20, global: false);
    }

    /// <summary>
    /// Ciclo de execucao do consumidor assincrono com confirmacao manual (Ack/Nack).
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.Received += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                var @event = JsonSerializer.Deserialize<TransactionCreatedEvent>(message);

                // Mensagem venonosa / payload corrompido: Nack sem requeue direciona para DLQ imediatamente
                if (@event is null)
                {
                    _logger.LogWarning("Mensagem com payload invalido ou corrompido recebida. Rejeitando para DLQ.");
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                _logger.LogInformation("Processando evento {EventId} para comerciante {MerchantId}...", @event.EventId, @event.MerchantId);

                // Cria um escopo isolado de injecao de dependencias para processamento seguro da mensagem
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IDailyConsolidatedRepository>();
                var cacheService = scope.ServiceProvider.GetRequiredService<IConsolidatedCacheService>();

                // 1. Garantia de Idempotencia: Verifica se o identificador do evento ja foi computado
                if (await repository.IsEventProcessedAsync(@event.EventId, stoppingToken))
                {
                    _logger.LogInformation("Evento {EventId} ja foi processado previamente. Descartando com Ack para evitar duplicidade de saldo.", @event.EventId);
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                var date = DateOnly.FromDateTime(@event.CreatedAt);

                // 2. Obter saldo existente ou inicializar consolidado novo
                var consolidated = await repository.GetAsync(@event.MerchantId, date, stoppingToken)
                                   ?? new DailyConsolidated(@event.MerchantId, date);

                // 3. Aplicar mutacao contabil no agregado de dominio
                consolidated.ApplyTransaction(@event.Amount, @event.Type);

                // 4. Gravar atomicamente no PostgreSQL e registrar evento na tabela de deduplicacao
                await repository.UpsertAsync(consolidated, stoppingToken);
                await repository.MarkEventProcessedAsync(@event.EventId, nameof(TransactionCreatedEvent), stoppingToken);

                // 5. Atualizar imediatamente o Redis (Write-Through)
                await cacheService.SetAsync(consolidated, cancellationToken: stoppingToken);

                // 6. Confirmar o processamento bem-sucedido para o broker RabbitMQ
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
                _logger.LogInformation("Consolidado atualizado com sucesso para comerciante {MerchantId} na data {Date}.", @event.MerchantId, date);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no processamento da mensagem do RabbitMQ. Encaminhando para DLQ para auditoria...");
                // Nao reenfileira na fila principal (requeue: false) para evitar loop infinito e saturacao de CPU
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Descarte seguro do canal AMQP ao finalizar o servico.
    /// </summary>
    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
