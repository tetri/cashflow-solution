using CashFlow.Transactions.Domain.Entities;
using CashFlow.Transactions.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Transactions.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementacao concreta do repositorio de transacoes financeiras utilizando Entity Framework Core e PostgreSQL.
/// Encapsula as operacoes de persistencia relacional da entidade Transaction, provendo metodos
/// para adicao assincrona e consultas otimizadas sem rastreamento de estado (AsNoTracking) para maior vazao.
/// </summary>
public class TransactionRepository : ITransactionRepository
{
    private readonly TransactionsDbContext _dbContext;

    /// <summary>
    /// Inicializa uma nova instancia do repositorio injetando o contexto do EF Core.
    /// </summary>
    /// <param name="dbContext">Contexto relacional de transacoes financeiras.</param>
    public TransactionRepository(TransactionsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Persiste uma nova transacao financeira no PostgreSQL e comita as alteracoes imediatamente.
    /// </summary>
    /// <param name="transaction">Entidade Transaction validada pelo dominio.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo da operacao.</param>
    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        await _dbContext.Transactions.AddAsync(transaction, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Localiza uma transacao existente atraves de seu identificador unico universal (UUID).
    /// Utiliza AsNoTracking para evitar sobrecarga de gerenciamento de memoria em leituras.
    /// </summary>
    /// <param name="id">UUID da transacao desejada.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// <returns>A transacao correspondente ou nulo caso nao seja encontrada.</returns>
    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    /// <summary>
    /// Localiza uma transacao previamente persistida utilizando a chave de idempotencia como filtro.
    /// Executa consulta indexada de alta performance amparada pelo indice unico parcial idx_transactions_idempotency_key.
    /// </summary>
    /// <param name="idempotencyKey">Chave de idempotencia associada a requisicao.</param>
    /// <param name="cancellationToken">Token para cancelamento cooperativo.</param>
    /// <returns>A entidade de transacao correspondente ou nulo se inexistente.</returns>
    public async Task<Transaction?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var chaveNormalizada = idempotencyKey.Trim();

        return await _dbContext.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IdempotencyKey == chaveNormalizada, cancellationToken);
    }
}
