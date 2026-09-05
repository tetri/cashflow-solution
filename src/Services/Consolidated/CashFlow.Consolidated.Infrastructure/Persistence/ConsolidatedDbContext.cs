using CashFlow.Consolidated.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidated.Infrastructure.Persistence;

/// <summary>
/// Contexto do Entity Framework Core responsavel pelo gerenciamento das duas tabelas
/// do modelo de leitura (Read-Side) do servico de consolidado diario.
///
/// Tabelas gerenciadas:
///   - daily_consolidated: armazena o saldo agregado por comerciante e data.
///   - processed_events: tabela de deduplicacao para garantia de idempotencia
///     no consumo de mensagens do RabbitMQ (evita duplicacao de saldo em retentativas).
///
/// Decisao arquitetural: utilizar um DbContext separado do contexto de escrita
/// (TransactionsDbContext) reforça o isolamento entre os lados do CQRS, impedindo
/// que migrações ou consultas de um servico impactem o outro.
/// </summary>
public sealed class ConsolidatedDbContext : DbContext
{
    /// <summary>
    /// Conjunto de entidades de saldo consolidado diario.
    /// </summary>
    public DbSet<DailyConsolidated> DailyConsolidated => Set<DailyConsolidated>();

    /// <summary>
    /// Conjunto de registros de eventos ja processados, utilizado para deduplicacao idempotente.
    /// </summary>
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    /// <summary>
    /// Inicializa o contexto com as opcoes de conexao fornecidas pelo contêiner de injecao de dependencia.
    /// </summary>
    public ConsolidatedDbContext(DbContextOptions<ConsolidatedDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Configura os mapeamentos relacionais das entidades do modelo de leitura.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Mapeamento da entidade de consolidado diario
        modelBuilder.Entity<DailyConsolidated>(entity =>
        {
            // Chave primaria composta por comerciante e data, garantindo unicidade do saldo diario
            entity.HasKey(e => new { e.MerchantId, e.Date });

            entity.ToTable("daily_consolidated");

            entity.Property(e => e.MerchantId)
                .HasColumnName("merchant_id")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Date)
                .HasColumnName("date")
                .IsRequired();

            entity.Property(e => e.TotalCredits)
                .HasColumnName("total_credits")
                .HasPrecision(18, 2)
                .IsRequired();

            entity.Property(e => e.TotalDebits)
                .HasColumnName("total_debits")
                .HasPrecision(18, 2)
                .IsRequired();

            // ClosingBalance e calculado (propriedade computada) e nao deve ser persistido
            entity.Ignore(e => e.ClosingBalance);

            entity.Property(e => e.TransactionCount)
                .HasColumnName("transaction_count")
                .IsRequired();

            entity.Property(e => e.LastUpdatedAt)
                .HasColumnName("last_updated_at")
                .IsRequired();

            // Version e utilizado para controle de concorrencia otimista (Optimistic Concurrency)
            entity.Property(e => e.Version)
                .HasColumnName("version")
                .IsConcurrencyToken();
        });

        // Mapeamento da tabela de deduplicacao de eventos processados
        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.ToTable("processed_events");

            entity.Property(e => e.EventId)
                .HasColumnName("event_id")
                .IsRequired();

            entity.Property(e => e.EventType)
                .HasColumnName("event_type")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.ProcessedAt)
                .HasColumnName("processed_at")
                .IsRequired();
        });
    }
}

/// <summary>
/// Entidade de controle de idempotencia que registra cada evento de dominio
/// processado com sucesso pelo Consolidated Worker.
///
/// Decisao arquitetural: a idempotencia e implementada via tabela relacional e nao
/// via Redis porque requer durabilidade transacional — uma falha de Redis nao deve
/// causar reprocessamento duplicado. A tabela processed_events participa da mesma
/// transacao que o Upsert do saldo, garantindo atomicidade absoluta.
/// </summary>
public sealed class ProcessedEvent
{
    /// <summary>
    /// Identificador unico do evento de dominio (EventId do IDomainEvent).
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// Nome estrutural do tipo de evento (ex: "TransactionCreatedEvent").
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Carimbo UTC de quando o evento foi computado com sucesso.
    /// </summary>
    public DateTime ProcessedAt { get; set; }
}
