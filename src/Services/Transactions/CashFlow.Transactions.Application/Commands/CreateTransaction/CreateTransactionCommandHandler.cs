using CashFlow.Shared.Domain.Events;
using CashFlow.Transactions.Domain.Entities;
using CashFlow.Transactions.Domain.Interfaces;

namespace CashFlow.Transactions.Application.Commands.CreateTransaction;

/// <summary>
/// Comando CQRS responsavel por transportar os dados de intencao de lancamento de credito ou debito.
/// Os comandos sao imutaveis e representam requisicoes de alteracao de estado no sistema.
/// </summary>
/// <param name="MerchantId">Identificador unico do comerciante.</param>
/// <param name="Amount">Valor financeiro a ser registrado.</param>
/// <param name="Type">Tipo de operacao ('Credit' ou 'Debit').</param>
/// <param name="Description">Historico ou justificativa do lancamento.</param>
public record CreateTransactionCommand(
    string MerchantId,
    decimal Amount,
    string Type,
    string Description
);

/// <summary>
/// Objeto de transferencia de dados retornado apos o processamento bem-sucedido do comando.
/// Contem o identificador gerado e os dados consolidados do lancamento.
/// </summary>
/// <param name="TransactionId">UUID da transacao gravada.</param>
/// <param name="MerchantId">Comerciante titular da transacao.</param>
/// <param name="Amount">Valor monetario persistido.</param>
/// <param name="Type">Tipo contabil do lancamento.</param>
/// <param name="CreatedAt">Carimbo de data/hora UTC da persistencia.</param>
public record CreateTransactionResult(
    Guid TransactionId,
    string MerchantId,
    decimal Amount,
    string Type,
    DateTime CreatedAt
);

/// <summary>
/// Manipulador de comando (CommandHandler) responsavel pelo fluxo transacional de criacao de lancamentos.
/// Orquestra a validacao de dominio, a persistencia relacional atomica e a emissao do evento
/// de integracao no RabbitMQ de forma desacoplada seguindo o padrao CQRS (Write-Side).
/// </summary>
public class CreateTransactionCommandHandler
{
    private readonly ITransactionRepository _repository;
    private readonly IEventPublisher _eventPublisher;

    /// <summary>
    /// Inicializa o manipulador injetando as dependencias de repositorio e publicador de eventos.
    /// </summary>
    /// <param name="repository">Repositorio de transacoes financeiras.</param>
    /// <param name="eventPublisher">Publicador resiliente de eventos no RabbitMQ.</param>
    public CreateTransactionCommandHandler(
        ITransactionRepository repository,
        IEventPublisher eventPublisher)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// Executa o comando de criacao de transacao de forma assincrona e transacional.
    /// </summary>
    /// <param name="command">Dados do comando de lancamento.</param>
    /// <param name="cancellationToken">Token de cancelamento da operacao.</param>
    /// <returns>Resultado com os detalhes da transacao criada.</returns>
    /// <exception cref="ArgumentException">Lancada quando o tipo de transacao informado for invalido.</exception>
    public async Task<CreateTransactionResult> HandleAsync(
        CreateTransactionCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Validacao sintatica e semantica do tipo de transacao (Credit / Debit)
        if (!Enum.TryParse<TransactionType>(command.Type, true, out var transactionType))
        {
            throw new ArgumentException($"Tipo de transacao invalido: '{command.Type}'. Tipos permitidos: 'Credit' ou 'Debit'.", nameof(command));
        }

        // 2. Criacao da entidade de dominio encapsulada garantindo integridade e invariantes
        var transaction = new Transaction(
            command.MerchantId,
            command.Amount,
            transactionType,
            command.Description
        );

        // 3. Persistencia estrita no banco relacional PostgreSQL
        await _repository.AddAsync(transaction, cancellationToken);

        // 4. Publicacao assincrona do evento de integracao para sincronizacao do consolidado
        var domainEvent = new TransactionCreatedEvent(
            transaction.Id,
            transaction.MerchantId,
            transaction.Amount,
            transaction.Type.ToString(),
            transaction.Description,
            transaction.CreatedAt
        );

        await _eventPublisher.PublishAsync(domainEvent, cancellationToken);

        // 5. Retorno do resultado estruturado para a camada de apresentacao (API)
        return new CreateTransactionResult(
            transaction.Id,
            transaction.MerchantId,
            transaction.Amount,
            transaction.Type.ToString(),
            transaction.CreatedAt
        );
    }
}
