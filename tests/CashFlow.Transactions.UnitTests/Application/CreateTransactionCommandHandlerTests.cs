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
/// Valida cenarios de criacao valida com persistencia e emissao de eventos, bem como violacao de invariantes de negocio.
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
    public async Task HandleAsync_WithValidCreditCommand_ShouldPersistAndPublishEvent()
    {
        // Arrange - Preparacao do comando de credito valido
        var command = new CreateTransactionCommand(
            MerchantId: "MERCH_123",
            Amount: 150.50m,
            Type: "Credit",
            Description: "Venda aprovada"
        );

        // Act - Execucao do comando
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert - Verificacao do retorno e chamadas de infraestrutura
        result.Should().NotBeNull();
        result.MerchantId.Should().Be("MERCH_123");
        result.Amount.Should().Be(150.50m);
        result.Type.Should().Be("Credit");

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
    /// Valida que lancamentos com valores menores ou iguais a zero disparam ArgumentException com mensagem em pt-BR.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task HandleAsync_WithInvalidAmount_ShouldThrowArgumentException(decimal invalidAmount)
    {
        // Arrange - Comando com valor monetario invalido
        var command = new CreateTransactionCommand("MERCH_123", invalidAmount, "Credit", "Venda");

        // Act - Execucao assincrona protegida para captura de excecao
        var act = async () => await _handler.HandleAsync(command);

        // Assert - A excecao de negocio em pt-BR deve ser disparada e nenhuma chamada de persistencia/publicacao deve ocorrer
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*maior que zero*");

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default(TransactionCreatedEvent)!);
    }
}
