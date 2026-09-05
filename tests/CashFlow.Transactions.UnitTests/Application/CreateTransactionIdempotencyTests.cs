using CashFlow.Shared.Domain.Events;
using CashFlow.Transactions.Application.Commands.CreateTransaction;
using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Domain.Entities;
using CashFlow.Transactions.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace CashFlow.Transactions.UnitTests.Application;

/// <summary>
/// Suite de testes unitarios focada no comportamento estrito do mecanismo de idempotencia do Write-Side.
/// Valida a deduplicacao de requisicoes repetidas, a deteccao de conflitos com dados divergentes
/// e o fluxo de novos lancamentos com e sem chave de idempotencia.
/// </summary>
public class CreateTransactionIdempotencyTests
{
    private readonly ITransactionRepository _repository = Substitute.For<ITransactionRepository>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly CreateTransactionCommandHandler _handler;

    public CreateTransactionIdempotencyTests()
    {
        _handler = new CreateTransactionCommandHandler(_repository, _eventPublisher);
    }

    /// <summary>
    /// Valida que ao reenviar uma transacao com a mesma chave e mesmos dados (MerchantId, Amount, Type),
    /// o manipulador retorna os dados originais com IsIdempotentDuplicate = true,
    /// sem chamar novamente o AddAsync do repositorio e sem publicar outro evento no RabbitMQ.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComMesmaChaveEMesmaCargaUtil_DeveRetornarRegistroOriginalSemPersistirNemPublicar()
    {
        // Arrange
        const string chave = "chave-idemp-12345";
        var transacaoOriginal = Transaction.CreateCredit("MERCH_001", 200.00m, "Venda balcao", chave);

        _repository.GetByIdempotencyKeyAsync(chave, Arg.Any<CancellationToken>())
            .Returns(transacaoOriginal);

        var comando = new CreateTransactionCommand(
            MerchantId: "MERCH_001",
            Amount: 200.00m,
            Type: "Credit",
            Description: "Venda balcao",
            IdempotencyKey: chave
        );

        // Act
        var resultado = await _handler.HandleAsync(comando, CancellationToken.None);

        // Assert
        resultado.Should().NotBeNull();
        resultado.IsIdempotentDuplicate.Should().BeTrue();
        resultado.TransactionId.Should().Be(transacaoOriginal.Id);
        resultado.MerchantId.Should().Be("MERCH_001");
        resultado.Amount.Should().Be(200.00m);
        resultado.Type.Should().Be("Credit");

        // Nenhuma persistencia adicional e nenhuma nova publicacao de evento devem ocorrer
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default(TransactionCreatedEvent)!);
    }

    /// <summary>
    /// Valida que a reutilizacao de uma mesma chave com MerchantId diferente lanca IdempotencyConflictException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComMesmaChaveEMerchantIdDivergente_DeveLancarIdempotencyConflictException()
    {
        // Arrange
        const string chave = "chave-conflito-merchant";
        var transacaoOriginal = Transaction.CreateCredit("MERCH_ORIGINAL", 150m, "Venda", chave);

        _repository.GetByIdempotencyKeyAsync(chave, Arg.Any<CancellationToken>())
            .Returns(transacaoOriginal);

        var comandoDivergente = new CreateTransactionCommand(
            MerchantId: "MERCH_DIVERGENTE",
            Amount: 150m,
            Type: "Credit",
            Description: "Venda",
            IdempotencyKey: chave
        );

        // Act
        var act = async () => await _handler.HandleAsync(comandoDivergente);

        // Assert
        await act.Should().ThrowAsync<IdempotencyConflictException>()
            .WithMessage("*ja foi utilizada com dados divergentes*");

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default(TransactionCreatedEvent)!);
    }

    /// <summary>
    /// Valida que a reutilizacao de uma mesma chave com valor monetario divergente lanca IdempotencyConflictException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComMesmaChaveEValorDivergente_DeveLancarIdempotencyConflictException()
    {
        // Arrange
        const string chave = "chave-conflito-valor";
        var transacaoOriginal = Transaction.CreateCredit("MERCH_001", 100.00m, "Venda", chave);

        _repository.GetByIdempotencyKeyAsync(chave, Arg.Any<CancellationToken>())
            .Returns(transacaoOriginal);

        var comandoDivergente = new CreateTransactionCommand(
            MerchantId: "MERCH_001",
            Amount: 100.50m, // Valor diferente
            Type: "Credit",
            Description: "Venda",
            IdempotencyKey: chave
        );

        // Act
        var act = async () => await _handler.HandleAsync(comandoDivergente);

        // Assert
        await act.Should().ThrowAsync<IdempotencyConflictException>()
            .WithMessage("*ja foi utilizada com dados divergentes*");

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default(TransactionCreatedEvent)!);
    }

    /// <summary>
    /// Valida que a reutilizacao de uma mesma chave com tipo de transacao invertido (Credito vs Debito) lanca conflito.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComMesmaChaveETipoDivergente_DeveLancarIdempotencyConflictException()
    {
        // Arrange
        const string chave = "chave-conflito-tipo";
        var transacaoOriginal = Transaction.CreateCredit("MERCH_001", 100.00m, "Venda", chave);

        _repository.GetByIdempotencyKeyAsync(chave, Arg.Any<CancellationToken>())
            .Returns(transacaoOriginal);

        var comandoDivergente = new CreateTransactionCommand(
            MerchantId: "MERCH_001",
            Amount: 100.00m,
            Type: "Debit", // Tipo invertido
            Description: "Venda",
            IdempotencyKey: chave
        );

        // Act
        var act = async () => await _handler.HandleAsync(comandoDivergente);

        // Assert
        await act.Should().ThrowAsync<IdempotencyConflictException>()
            .WithMessage("*ja foi utilizada com dados divergentes*");

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default(TransactionCreatedEvent)!);
    }

    /// <summary>
    /// Valida que ao fornecer uma chave nova nao cadastrada, a transacao e persistida, o evento e emitido
    /// e o resultado indica IsIdempotentDuplicate = false.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComChaveNovaInexistente_DevePersistirEPublicarRetornandoFalse()
    {
        // Arrange
        const string chaveNova = "chave-inedita-999";
        _repository.GetByIdempotencyKeyAsync(chaveNova, Arg.Any<CancellationToken>())
            .Returns((Transaction?)null);

        var comando = new CreateTransactionCommand(
            MerchantId: "MERCH_001",
            Amount: 350.00m,
            Type: "Credit",
            Description: "Novo recebimento",
            IdempotencyKey: chaveNova
        );

        // Act
        var resultado = await _handler.HandleAsync(comando, CancellationToken.None);

        // Assert
        resultado.Should().NotBeNull();
        resultado.IsIdempotentDuplicate.Should().BeFalse();
        resultado.Amount.Should().Be(350.00m);

        await _repository.Received(1).AddAsync(Arg.Is<Transaction>(t =>
            t.IdempotencyKey == chaveNova &&
            t.Amount == 350.00m
        ), Arg.Any<CancellationToken>());

        await _eventPublisher.Received(1).PublishAsync(Arg.Is<TransactionCreatedEvent>(e =>
            e.Amount == 350.00m
        ), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Valida que requisicoes sem chave de idempotencia (nula ou vazia) sao tratadas como operacoes
    /// transacionais independentes, persistindo e publicando sem consulta previa de chave.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_SemChaveDeIdempotencia_DeveProcessarComoOperacoesIndependentes(string? chaveNulaOuVazia)
    {
        // Arrange
        var comando = new CreateTransactionCommand(
            MerchantId: "MERCH_001",
            Amount: 80.00m,
            Type: "Credit",
            Description: "Venda sem chave",
            IdempotencyKey: chaveNulaOuVazia
        );

        // Act
        var resultado = await _handler.HandleAsync(comando, CancellationToken.None);

        // Assert
        resultado.Should().NotBeNull();
        resultado.IsIdempotentDuplicate.Should().BeFalse();

        // Nao deve consultar chave nula no repositorio
        await _repository.DidNotReceiveWithAnyArgs().GetByIdempotencyKeyAsync(default!);

        await _repository.Received(1).AddAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(Arg.Any<TransactionCreatedEvent>(), Arg.Any<CancellationToken>());
    }
}
