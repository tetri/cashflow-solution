using CashFlow.Transactions.Domain.Entities;

namespace CashFlow.Transactions.Domain.Interfaces;

/// <summary>
/// Contrato de repositorio para a persistencia de transacoes financeiras no PostgreSQL.
/// Na Clean Architecture, as interfaces pertencem ao Dominio para inverter a dependencia,
/// isolando as regras de negocio dos detalhes tecnologicos do banco relacional ou ORM.
/// </summary>
public interface ITransactionRepository
{
    /// <summary>
    /// Persiste de forma assincrona uma nova entidade de transacao no banco relacional.
    /// </summary>
    /// <param name="transaction">Instancia validada da transacao a ser gravada.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo da operacao.</param>
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera uma transacao persistida a partir de seu identificador unico universal (UUID).
    /// </summary>
    /// <param name="id">Identificador UUID da transacao.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// <returns>A entidade encontrada ou nulo se inexistente.</returns>
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta uma transacao pre-existente com base na chave de idempotencia fornecida.
    /// Permite ao servico de aplicacao verificar se a operacao ja foi executada anteriormente,
    /// prevenindo a duplicacao de lancamentos e a emissao redundante de eventos no barramento.
    /// </summary>
    /// <param name="idempotencyKey">Chave unica de identificacao da requisicao.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// <returns>A entidade correspondente ou nulo caso nao haja registro previo com a chave.</returns>
    Task<Transaction?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstracao para publicacao assincrona de eventos de dominio.
/// Isola a aplicacao dos detalhes de conexao, serializacao e topologia do RabbitMQ.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publica uma mensagem de evento no broker de mensagens de forma resiliente.
    /// </summary>
    /// <typeparam name="T">Tipo da mensagem de evento.</typeparam>
    /// <param name="event">Objeto contendo os dados do evento.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : class;
}
