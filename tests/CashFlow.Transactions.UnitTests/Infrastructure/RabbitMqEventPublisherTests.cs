using System.Net.Sockets;
using CashFlow.Shared.Domain.Events;
using CashFlow.Transactions.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;
using Xunit;

namespace CashFlow.Transactions.UnitTests.Infrastructure;

/// <summary>
/// Suite de testes unitarios para o publicador de eventos RabbitMqEventPublisher.
/// Valida a configuracao dos metadados AMQP, a vinculacao de CorrelationId,
/// a aplicacao do pipeline de resiliencia Polly sob falhas de rede e o descarte de conexao.
/// </summary>
public class RabbitMqEventPublisherTests : IDisposable
{
    private readonly IConnection _connection = Substitute.For<IConnection>();
    private readonly IModel _channel = Substitute.For<IModel>();
    private readonly IBasicProperties _basicProperties = Substitute.For<IBasicProperties>();
    private readonly ILogger<RabbitMqEventPublisher> _logger = Substitute.For<ILogger<RabbitMqEventPublisher>>();
    private readonly RabbitMqEventPublisher _publisher;

    public RabbitMqEventPublisherTests()
    {
        _channel.CreateBasicProperties().Returns(_basicProperties);
        _connection.CreateModel().Returns(_channel);

        _publisher = new RabbitMqEventPublisher(_connection, _logger);
    }

    public void Dispose()
    {
        _publisher.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Valida que um evento valido e publicado na exchange cashflow.events com routingKey adequada
    /// e que as propriedades AMQP de persistencia, encoding e CorrelationId sao preenchidas corretamente.
    /// </summary>
    [Fact]
    public async Task PublishAsync_ComEventoValido_DevePublicarComPropriedadesCorretas()
    {
        // Arrange
        var evento = new TransactionCreatedEvent(
            TransactionId: Guid.NewGuid(),
            MerchantId: "MERCH_TEST",
            Amount: 300.00m,
            Type: "Credit",
            Description: "Venda teste",
            CreatedAt: DateTime.UtcNow
        );

        // Act
        await _publisher.PublishAsync(evento, CancellationToken.None);

        // Assert - Verifica se BasicPublish da interface IModel foi acionado com os parametros esperados
        _channel.Received(1).BasicPublish(
            exchange: "cashflow.events",
            routingKey: "transaction.transactioncreatedevent",
            mandatory: false,
            basicProperties: _basicProperties,
            body: Arg.Any<ReadOnlyMemory<byte>>()
        );

        // Verifica a configuracao das propriedades de durabilidade e correlacao
        _basicProperties.Persistent.Should().BeTrue();
        _basicProperties.DeliveryMode.Should().Be(2);
        _basicProperties.ContentType.Should().Be("application/json");
        _basicProperties.ContentEncoding.Should().Be("utf-8");
        _basicProperties.CorrelationId.Should().Be(evento.EventId.ToString());
        _basicProperties.MessageId.Should().Be(evento.EventId.ToString());
    }

    /// <summary>
    /// Valida que a tentativa de publicar um evento nulo dispara ArgumentNullException imediatamente.
    /// </summary>
    [Fact]
    public async Task PublishAsync_ComEventoNulo_DeveLancarArgumentNullException()
    {
        // Act
        var act = async () => await _publisher.PublishAsync<TransactionCreatedEvent>(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
        _channel.DidNotReceiveWithAnyArgs().BasicPublish(default!, default!, default, default!, default);
    }

    /// <summary>
    /// Valida que sob falha transiente de rede (SocketException), a politica de Retry do Polly v8
    /// intercepta o erro e realiza uma nova tentativa com sucesso.
    /// </summary>
    [Fact]
    public async Task PublishAsync_ComFalhaTransienteDeRede_DeveRetentarEPublicarComSucesso()
    {
        // Arrange
        var evento = new TransactionCreatedEvent(
            TransactionId: Guid.NewGuid(),
            MerchantId: "MERCH_RETRY",
            Amount: 120.00m,
            Type: "Debit",
            Description: "Pagamento com retry",
            CreatedAt: DateTime.UtcNow
        );

        var tentativa = 0;
        _channel.When(c => c.BasicPublish(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<IBasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>()
        )).Do(_ =>
        {
            tentativa++;
            if (tentativa == 1)
            {
                // Primeira tentativa falha simulando instabilidade de socket de rede
                throw new SocketException((int)SocketError.ConnectionReset);
            }
            // Segunda tentativa transcorre com sucesso
        });

        // Act
        await _publisher.PublishAsync(evento, CancellationToken.None);

        // Assert - A publicacao no IModel deve ter ocorrido duas vezes (1 falha + 1 retry bem-sucedido)
        _channel.Received(2).BasicPublish(
            exchange: "cashflow.events",
            routingKey: "transaction.transactioncreatedevent",
            mandatory: false,
            basicProperties: _basicProperties,
            body: Arg.Any<ReadOnlyMemory<byte>>()
        );
    }

    /// <summary>
    /// Valida que a chamada ao Dispose() libera ordenadamente o canal AMQP e a conexao subjacente.
    /// </summary>
    [Fact]
    public void Dispose_DeveEncerrarCanalEConexao()
    {
        // Act
        _publisher.Dispose();

        // Assert
        _channel.Received(1).Dispose();
        _connection.Received(1).Dispose();
    }
}
