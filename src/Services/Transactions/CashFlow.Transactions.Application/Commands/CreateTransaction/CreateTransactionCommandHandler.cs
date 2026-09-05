using CashFlow.Shared.Domain.Events;
using CashFlow.Transactions.Application.Common;
using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Domain.Entities;
using CashFlow.Transactions.Domain.Interfaces;

namespace CashFlow.Transactions.Application.Commands.CreateTransaction;

/// <summary>
/// Comando CQRS responsavel por transportar os dados de intencao de lancamento de credito ou debito.
/// Os comandos sao imutaveis e representam intencoes de alteracao de estado no sistema financeiro.
/// </summary>
/// <param name="MerchantId">Identificador unico do comerciante titular do caixa.</param>
/// <param name="Amount">Valor financeiro a ser registrado na operacao.</param>
/// <param name="Type">Tipo de operacao ('Credit' para credito ou 'Debit' para debito).</param>
/// <param name="Description">Historico ou detalhamento contabil do lancamento.</param>
/// <param name="IdempotencyKey">Chave opcional para garantia de idempotencia na operacao.</param>
public record CreateTransactionCommand(
    string MerchantId,
    decimal Amount,
    string Type,
    string Description,
    string? IdempotencyKey = null
);

/// <summary>
/// Objeto de transferencia de dados retornado apos o processamento bem-sucedido ou idempotente do comando.
/// Contem os dados consolidados da transacao e indica expressamente se houve duplicacao idempotente.
/// </summary>
/// <param name="TransactionId">UUID da transacao gravada ou recuperada por idempotencia.</param>
/// <param name="MerchantId">Comerciante titular da transacao.</param>
/// <param name="Amount">Valor monetario da operacao.</param>
/// <param name="Type">Tipo contabil do lancamento ('Credit' ou 'Debit').</param>
/// <param name="CreatedAt">Carimbo de data/hora UTC do registro original.</param>
/// <param name="IsIdempotentDuplicate">Indica se a requisicao foi reconhecida como duplicata idempotente ja persistida.</param>
public record CreateTransactionResult(
    Guid TransactionId,
    string MerchantId,
    decimal Amount,
    string Type,
    DateTime CreatedAt,
    bool IsIdempotentDuplicate
);

/// <summary>
/// Manipulador de comando (CommandHandler) responsavel pelo ciclo transacional de gravacao no Write-Side.
/// Implementa regras estritas de saneamento de emojis, verificacao de idempotencia, persistencia
/// atomica no banco PostgreSQL e emissao de eventos assincronos de integracao no RabbitMQ.
/// </summary>
public class CreateTransactionCommandHandler
{
    private readonly ITransactionRepository _repository;
    private readonly IEventPublisher _eventPublisher;

    /// <summary>
    /// Inicializa o manipulador injetando o repositorio relacional e o publicador resiliente de eventos.
    /// </summary>
    /// <param name="repository">Contrato de persistencia de transacoes.</param>
    /// <param name="eventPublisher">Publicador resiliente de mensageria AMQP.</param>
    public CreateTransactionCommandHandler(
        ITransactionRepository repository,
        IEventPublisher eventPublisher)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// Executa o processamento do comando de lancamento de forma assincrona e idempotente.
    /// </summary>
    /// <param name="command">Dados do comando recebido.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo da requisicao.</param>
    /// <returns>Resultado estruturado da transacao processada.</returns>
    /// <exception cref="ArgumentException">Lancada quando ha violacao de validacao de entrada ou emojis detectados.</exception>
    /// <exception cref="IdempotencyConflictException">Lancada quando a mesma chave de idempotencia e reutilizada com dados divergentes.</exception>
    public async Task<CreateTransactionResult> HandleAsync(
        CreateTransactionCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Validacao rigorosa de ausencia de emojis em conformidade com a Regra R3
        if (EmojiDetector.ContemEmoji(command.MerchantId) ||
            EmojiDetector.ContemEmoji(command.Description) ||
            EmojiDetector.ContemEmoji(command.Type) ||
            EmojiDetector.ContemEmoji(command.IdempotencyKey))
        {
            throw new ArgumentException("Caracteres invalidos ou emojis detectados. Apenas texto em portugues do Brasil e permitido.");
        }

        // 2. Validacao semantica e sintatica do tipo de transacao informado
        if (!Enum.TryParse<TransactionType>(command.Type, true, out var transactionType) ||
            !Enum.IsDefined(transactionType))
        {
            throw new ArgumentException($"Tipo de transacao invalido: '{command.Type}'. Tipos permitidos: 'Credit' ou 'Debit'.", nameof(command));
        }

        // 3. Verificacao de idempotencia quando a chave correspondente for informada
        var chaveIdempotenciaNormalizada = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : command.IdempotencyKey.Trim();

        if (chaveIdempotenciaNormalizada is not null)
        {
            var transacaoExistente = await _repository.GetByIdempotencyKeyAsync(chaveIdempotenciaNormalizada, cancellationToken);

            if (transacaoExistente is not null)
            {
                // Verifica a paridade estrita dos dados entre a transacao original e a nova tentativa
                var mesmoComerciante = string.Equals(transacaoExistente.MerchantId, command.MerchantId?.Trim(), StringComparison.Ordinal);
                var mesmoValor = transacaoExistente.Amount == command.Amount;
                var mesmoTipo = transacaoExistente.Type == transactionType;

                if (mesmoComerciante && mesmoValor && mesmoTipo)
                {
                    // Idempotencia preservada: retorna o lancamento original sem duplicar gravacao e sem republicar evento
                    return new CreateTransactionResult(
                        transacaoExistente.Id,
                        transacaoExistente.MerchantId,
                        transacaoExistente.Amount,
                        transacaoExistente.Type.ToString(),
                        transacaoExistente.CreatedAt,
                        IsIdempotentDuplicate: true
                    );
                }

                // Chave reutilizada com divergencia nos dados: violacao de conflito de idempotencia
                throw new IdempotencyConflictException("A chave de idempotencia fornecida ja foi utilizada com dados divergentes.");
            }
        }

        // 4. Criacao da entidade Transaction atraves dos metodos de fabrica expressivos do dominio
        Transaction transacao = transactionType == TransactionType.Credit
            ? Transaction.CreateCredit(command.MerchantId, command.Amount, command.Description, chaveIdempotenciaNormalizada)
            : Transaction.CreateDebit(command.MerchantId, command.Amount, command.Description, chaveIdempotenciaNormalizada);

        // 5. Persistencia relacional no PostgreSQL
        await _repository.AddAsync(transacao, cancellationToken);

        // 6. Publicacao assincrona do evento de dominio no barramento RabbitMQ
        var eventoDominio = new TransactionCreatedEvent(
            transacao.Id,
            transacao.MerchantId,
            transacao.Amount,
            transacao.Type.ToString(),
            transacao.Description,
            transacao.CreatedAt
        );

        await _eventPublisher.PublishAsync(eventoDominio, cancellationToken);

        // 7. Retorno do resultado com indicacao de novo lancamento
        return new CreateTransactionResult(
            transacao.Id,
            transacao.MerchantId,
            transacao.Amount,
            transacao.Type.ToString(),
            transacao.CreatedAt,
            IsIdempotentDuplicate: false
        );
    }
}
