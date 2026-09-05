using CashFlow.Transactions.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Transactions.Infrastructure.Persistence;

/// <summary>
/// Contexto de banco de dados relacional do Entity Framework Core para o servico de lancamentos (Write-Side).
/// Encapsula a sessao de comunicacao com o PostgreSQL, gerenciando a transacionalidade
/// e o rastreamento das operacoes de insercao e consulta da entidade Transaction.
/// Na Clean Architecture, o DbContext reside na camada de Infraestrutura, mantendo o Dominio isolado.
/// </summary>
public class TransactionsDbContext : DbContext
{
    /// <summary>
    /// Construtor que recebe as opcoes configuradas do contexto (ex.: string de conexao Npgsql).
    /// </summary>
    /// <param name="options">Opcoes de configuracao do DbContext gerenciadas pelo container de DI.</param>
    public TransactionsDbContext(DbContextOptions<TransactionsDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Conjunto de dados que representa a tabela relacional 'transactions'.
    /// </summary>
    public DbSet<Transaction> Transactions => Set<Transaction>();

    /// <summary>
    /// Aplica as configuracoes de mapeamento objeto-relacional (Fluent API)
    /// definidas nas classes que implementam IEntityTypeConfiguration neste assembly.
    /// Esta abordagem modulariza as regras de mapeamento, evitando concentracao excessiva de codigo no DbContext.
    /// </summary>
    /// <param name="modelBuilder">Construtor do modelo de metadados do EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Varre o assembly de infraestrutura e aplica automaticamente todas as configuracoes de entidade
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransactionsDbContext).Assembly);
    }
}
