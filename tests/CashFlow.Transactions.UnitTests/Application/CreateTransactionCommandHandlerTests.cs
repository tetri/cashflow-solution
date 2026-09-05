using CashFlow.Shared.Domain.Events;
using CashFlow.Transactions.Application.Commands.CreateTransaction;
using CashFlow.Transactions.Domain.Entities;
using CashFlow.Transactions.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace CashFlow.Transactions.UnitTests.Application;

/// <summary>
/// Suite de testes unitarios para o manipulador de comandos de lancamento (CreateTransactionCommandHandler).
/// Valida fluxos de sucesso para Credito e Debito, violacao de regras de negocio,
/// deteccao estrita de emojis nos campos de entrada e isolamento de falhas de persistencia.
/// </summary>
public class CreateTransactionCommandHandlerTests
{
    private readonly ITransactionRepository _repository = Substitute.For<ITransactionRepository>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly CreateTransactionCommandHandler _handler;

    /// <summary>
    /// Instancia o manipulador sob teste com os dubles de teste (mocks via NSubstitute).
    /// </summary>
    public CreateTransactionCommandHandlerTests()
    {
        _handler = new CreateTransactionCommandHandler(_repository, _eventPublisher);
    }

    /// <summary>
    /// Valida que um comando valido de credito persiste a transacao no repositorio e publica o evento no RabbitMQ.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComComandoValidoDeCredito_DevePersistirEPublicarEvento()
    {
        // Arrange - Preparacao do comando de credito valido
        var command = new CreateTransactionCommand(
            MerchantId: "MERCH_123",
            Amount: 150.50m,
            Type: "Credit",
            Description: "Venda aprovada no balcao"
        );

        // Act - Execucao do comando
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert - Verificacao do retorno e chamadas de infraestrutura
        result.Should().NotBeNull();
        result.MerchantId.Should().Be("MERCH_123");
        result.Amount.Should().Be(150.50m);
        result.Type.Should().Be("Credit");
        result.IsIdempotentDuplicate.Should().BeFalse();

        // Confirma que a transacao foi persistida no repositorio exatamente uma vez
        await _repository.Received(1).AddAsync(Arg.Is<Transaction>(t =>
            t.MerchantId == "MERCH_123" &&
            t.Amount == 150.50m &&
            t.Type == TransactionType.Credit
        ), Arg.Any<CancellationToken>());

        // Confirma que o evento de dominio foi publicado no broker exatamente uma vez
        await _eventPublisher.Received(1).PublishAsync(Arg.Is<TransactionCreatedEvent>(e =>
            e.MerchantId == "MERCH_123" &&
            e.Amount == 150.50m &&
            e.Type == "Credit"
        ), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Valida que um comando valido de debito persiste a transacao no repositorio e publica o evento correspondente.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComComandoValidoDeDebito_DevePersistirEPublicarEvento()
    {
        // Arrange
        var command = new CreateTransactionCommand(
            MerchantId: "MERCH_123",
            Amount: 75.20m,
            Type: "Debit",
            Description: "Pagamento de conta de energia"
        );

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.MerchantId.Should().Be("MERCH_123");
        result.Amount.Should().Be(75.20m);
        result.Type.Should().Be("Debit");
        result.IsIdempotentDuplicate.Should().BeFalse();

        await _repository.Received(1).AddAsync(Arg.Is<Transaction>(t =>
            t.MerchantId == "MERCH_123" &&
            t.Amount == 75.20m &&
            t.Type == TransactionType.Debit
        ), Arg.Any<CancellationToken>());

        await _eventPublisher.Received(1).PublishAsync(Arg.Is<TransactionCreatedEvent>(e =>
            e.MerchantId == "MERCH_123" &&
            e.Amount == 75.20m &&
            e.Type == "Debit"
        ), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Valida que lancamentos com valores menores ou iguais a zero disparam ArgumentException com mensagem em pt-BR.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task HandleAsync_ComValorInvalido_DeveLancarArgumentException(decimal invalidAmount)
    {
        // Arrange
        var command = new CreateTransactionCommand("MERCH_123", invalidAmount, "Credit", "Venda");

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*maior que zero*");

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default(TransactionCreatedEvent)!);
    }

    /// <summary>
    /// Valida que tipo de operacao invalido (diferente de Credit e Debit) dispara ArgumentException.
    /// </summary>
    [Theory]
    [InlineData("Invalido")]
    [InlineData("Transferencia")]
    [InlineData("Pix")]
    public async Task HandleAsync_ComTipoDeTransacaoInvalido_DeveLancarArgumentException(string tipoInvalido)
    {
        // Arrange
        var command = new CreateTransactionCommand("MERCH_123", 100m, tipoInvalido, "Venda balcao");

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Tipo de transacao invalido*");

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default(TransactionCreatedEvent)!);
    }

    /// <summary>
    /// Valida que a presenca de emojis no MerchantId dispara ArgumentException com mensagem em pt-BR.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComEmojiNoMerchantId_DeveLancarArgumentException()
    {
        // Arrange - String contendo emoji codificado via surrogate pair
        var merchantComEmoji = "MERCH_" + char.ConvertFromUtf32(0x1F600);
        var command = new CreateTransactionCommand(merchantComEmoji, 100m, "Credit", "Venda regular");

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*emojis detectados*");

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default(TransactionCreatedEvent)!);
    }

    /// <summary>
    /// Valida que a presenca de emojis na Description dispara ArgumentException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComEmojiNaDescricao_DeveLancarArgumentException()
    {
        // Arrange
        var descricaoComEmoji = "Venda de produto " + char.ConvertFromUtf32(0x1F680);
        var command = new CreateTransactionCommand("MERCH_123", 100m, "Credit", descricaoComEmoji);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*emojis detectados*");
    }

    /// <summary>
    /// Valida que a presenca de emojis no campo Type dispara ArgumentException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComEmojiNoTipo_DeveLancarArgumentException()
    {
        // Arrange
        var tipoComEmoji = "Credit" + char.ConvertFromUtf32(0x1F44D);
        var command = new CreateTransactionCommand("MERCH_123", 100m, tipoComEmoji, "Venda");

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*emojis detectados*");
    }

    /// <summary>
    /// Valida que a presenca de emojis na IdempotencyKey dispara ArgumentException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ComEmojiNaIdempotencyKey_DeveLancarArgumentException()
    {
        // Arrange
        var chaveComEmoji = "key-" + char.ConvertFromUtf32(0x1F525);
        var command = new CreateTransactionCommand("MERCH_123", 100m, "Credit", "Venda", chaveComEmoji);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*emojis detectados*");
    }

    /// <summary>
    /// Valida que caso o repositorio lance uma excecao de conexao, ela e propagada e nenhum evento e emitido.
    /// </summary>
    [Fact]
    public async Task HandleAsync_QuandoRepositorioFalha_DevePropagarExcecaoESemPublicarEvento()
    {
        // Arrange
        var command = new CreateTransactionCommand("MERCH_123", 100m, "Credit", "Venda regular");
        _repository.AddAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Falha de conexao com o PostgreSQL"));

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PostgreSQL*");

        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default(TransactionCreatedEvent)!);
    }
}
