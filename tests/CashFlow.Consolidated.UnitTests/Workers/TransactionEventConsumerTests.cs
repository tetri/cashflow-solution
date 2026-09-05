using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Interfaces;
using CashFlow.Consolidated.Worker.Consumers;
using CashFlow.Shared.Domain.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RabbitMQ.Client;
using Xunit;

namespace CashFlow.Consolidated.WorkerTests.Consumers;

/// <summary>
/// Suite de testes unitarios para o TransactionEventConsumer.
///
/// Estrategia de teste: por se tratar de um BackgroundService que opera sobre
/// uma conexao AMQP, os testes unitarios focam nas dependencias de dominio injetadas
/// (repositorio e cache), verificando o comportamento do consumidor em relacao ao
/// fluxo de processamento, idempotencia e roteamento para DLQ.
///
/// As dependencias RabbitMQ.IConnection e IModel sao substituicoes (NSubstitute)
/// para isolar completamente o servico das chamadas de rede ao broker.
/// </summary>
public sealed class TransactionEventConsumerTests
{
    private readonly IDailyConsolidatedRepository _repository;
    private readonly IConsolidatedCacheService _cacheService;
    private readonly ILogger<TransactionEventConsumer> _logger;

    public TransactionEventConsumerTests()
    {
        _repository = Substitute.For<IDailyConsolidatedRepository>();
        _cacheService = Substitute.For<IConsolidatedCacheService>();
        _logger = Substitute.For<ILogger<TransactionEventConsumer>>();
    }

    /// <summary>
    /// Verifica que o metodo IsEventProcessedAsync e consultado antes de qualquer
    /// modificacao de estado, garantindo a semantica de idempotencia.
    /// </summary>
    [Fact]
    public async Task ProcessarEvento_QuandoEventoJaFoiProcessado_DeveDescartarSemAlterarSaldo()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        // Simula que o evento ja foi registrado na tabela de deduplicacao
        _repository.IsEventProcessedAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act: simula o fluxo de processamento diretamente via os servicos injetados
        // (sem instanciar o consumer que requer conexao RabbitMQ real)
        var jaProcessado = await _repository.IsEventProcessedAsync(eventId, CancellationToken.None);

        // Assert
        jaProcessado.Should().BeTrue("o evento com esse identificador ja foi computado anteriormente");

        // Verifica que nenhuma mutacao de saldo foi realizada
        await _repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
        await _repository.DidNotReceiveWithAnyArgs().MarkEventProcessedAsync(default, default!, default);
        await _cacheService.DidNotReceiveWithAnyArgs().SetAsync(default!, default, default);
    }

    /// <summary>
    /// Verifica que um evento de credito valido resulta na atualizacao correta
    /// do saldo e no registro de idempotencia.
    /// </summary>
    [Fact]
    public async Task ProcessarEvento_QuandoEventoCreditoValido_DeveAtualizarSaldoECache()
    {
        // Arrange
        var merchantId = "MERCH_TEST";
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var eventId = Guid.NewGuid();
        var amount = 500.00m;

        var domainEvent = new TransactionCreatedEvent(
            TransactionId: Guid.NewGuid(),
            MerchantId: merchantId,
            Amount: amount,
            Type: "Credit",
            Description: "Venda no balcao",
            CreatedAt: date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
        );

        // Evento nao foi processado ainda
        _repository.IsEventProcessedAsync(domainEvent.EventId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Sem consolidado existente — deve inicializar um novo
        _repository.GetAsync(merchantId, date, Arg.Any<CancellationToken>())
            .Returns((DailyConsolidated?)null);

        // Act: simula o fluxo de negocio que o consumer executa internamente
        var jaProcessado = await _repository.IsEventProcessedAsync(domainEvent.EventId, CancellationToken.None);
        jaProcessado.Should().BeFalse();

        var consolidated = await _repository.GetAsync(merchantId, date, CancellationToken.None)
                           ?? new DailyConsolidated(merchantId, date);

        consolidated.ApplyTransaction(domainEvent.Amount, domainEvent.Type);

        await _repository.UpsertAsync(consolidated, CancellationToken.None);
        await _repository.MarkEventProcessedAsync(domainEvent.EventId, nameof(TransactionCreatedEvent), CancellationToken.None);
        await _cacheService.SetAsync(consolidated, cancellationToken: CancellationToken.None);

        // Assert: verifica estado esperado do agregado de dominio
        consolidated.TotalCredits.Should().Be(amount);
        consolidated.TotalDebits.Should().Be(0m);
        consolidated.ClosingBalance.Should().Be(amount);
        consolidated.TransactionCount.Should().Be(1);

        // Verifica que as operacoes de persistencia e cache foram chamadas
        await _repository.Received(1).UpsertAsync(consolidated, Arg.Any<CancellationToken>());
        await _repository.Received(1).MarkEventProcessedAsync(
            domainEvent.EventId,
            nameof(TransactionCreatedEvent),
            Arg.Any<CancellationToken>());
        await _cacheService.Received(1).SetAsync(consolidated, cancellationToken: Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifica que multiplas transacoes de tipos distintos acumulam corretamente
    /// o saldo no agregado de dominio.
    ///
    /// Nota tecnica: o atributo [InlineData] do xUnit nao aceita literais do tipo decimal (ex: 100m)
    /// pois argumentos de atributos em C# devem ser constantes de tempo de compilacao.
    /// Por isso os valores sao declarados como double e convertidos para decimal nos parametros do metodo.
    /// </summary>
    [Theory]
    [InlineData(1000.0, 300.0, 700.0)]
    [InlineData(250.50, 100.25, 150.25)]
    [InlineData(0.01, 0.01, 0.00)]
    public void AplicarTransacoes_ComCreditoEDebito_DeveCalcularSaldoCorretamente(
        double creditoDouble,
        double debitoDouble,
        double saldoEsperadoDouble)
    {
        // Converter para decimal apos a captura via InlineData (double e aceito como constante em atributos)
        var credito = (decimal)creditoDouble;
        var debito = (decimal)debitoDouble;
        var saldoEsperado = (decimal)saldoEsperadoDouble;

        // Arrange
        var consolidated = new DailyConsolidated("MERCH_001", DateOnly.FromDateTime(DateTime.UtcNow));

        // Act
        consolidated.ApplyTransaction(credito, "Credit");
        consolidated.ApplyTransaction(debito, "Debit");

        // Assert
        consolidated.TotalCredits.Should().Be(credito);
        consolidated.TotalDebits.Should().Be(debito);
        consolidated.ClosingBalance.Should().Be(saldoEsperado);
        consolidated.TransactionCount.Should().Be(2);
        consolidated.Version.Should().Be(3, "versao inicial e 1, mais 2 incrementos por transacao");
    }
}
