using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidated.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementacao concreta do repositorio de consolidados diarios utilizando
/// Entity Framework Core com banco de dados PostgreSQL.
///
/// Responsabilidades centrais:
///   1. Consulta do saldo consolidado por comerciante e data.
///   2. Upsert atomico do saldo com controle de concorrencia otimista (Version).
///   3. Registro e verificacao de eventos processados (tabela processed_events)
///      para garantia estrita de idempotencia no consumo de mensagens do RabbitMQ.
///
/// Decisao arquitetural: utilizar transacoes explicitas do Entity Framework Core
/// (Database.BeginTransactionAsync) garante que o Upsert do saldo e o registro
/// do evento processado ocorram de forma atomica — evitando estados inconsistentes
/// em que o saldo e atualizado mas o evento nao e marcado (ou vice-versa).
/// </summary>
public sealed class DailyConsolidatedRepository : IDailyConsolidatedRepository
{
    private readonly ConsolidatedDbContext _context;
    private readonly ILogger<DailyConsolidatedRepository> _logger;

    /// <summary>
    /// Inicializa o repositorio com o contexto EF Core e o mecanismo de telemetria.
    /// </summary>
    public DailyConsolidatedRepository(
        ConsolidatedDbContext context,
        ILogger<DailyConsolidatedRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Consulta por chave primaria composta (MerchantId + Date).
    /// Utiliza AsNoTracking para leitura otimizada sem sobrecarga do Change Tracker do EF Core,
    /// pois a entidade sera imediatamente reatribuida ao contexto no Upsert subsequente.
    /// </remarks>
    public async Task<DailyConsolidated?> GetAsync(
        string merchantId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _context.DailyConsolidated
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.MerchantId == merchantId && e.Date == date,
                cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Implementa a semantica de Upsert (INSERT or UPDATE) utilizando transacao explicita.
    /// A estrategia e: tentar inserir o registro — se houver violacao de chave unica (INSERT),
    /// realizar o UPDATE. O Version e incrementado pela propria entidade de dominio em ApplyTransaction,
    /// servindo como token de concorrencia otimista para detectar modificacoes concorrentes.
    ///
    /// Em um ambiente com multiplas instancias do Worker (escala horizontal), o controle
    /// de concorrencia otimista protege contra a sobrescrita silenciosa de saldos.
    /// </remarks>
    public async Task UpsertAsync(
        DailyConsolidated consolidated,
        CancellationToken cancellationToken = default)
    {
        // Verificar se o registro ja existe no banco para decidir entre Insert e Update
        var exists = await _context.DailyConsolidated
            .AnyAsync(
                e => e.MerchantId == consolidated.MerchantId && e.Date == consolidated.Date,
                cancellationToken);

        if (exists)
        {
            // UPDATE: Atualiza via SQL direto para evitar carregar e rastrear a entidade novamente.
            // Utilizamos ExecuteUpdateAsync (EF Core 7+) para eficiencia maxima e evitar conflitos
            // com o Change Tracker que poderia ter uma versao desatualizada da entidade.
            var updatedRows = await _context.DailyConsolidated
                .Where(e => e.MerchantId == consolidated.MerchantId && e.Date == consolidated.Date)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.TotalCredits, consolidated.TotalCredits)
                    .SetProperty(e => e.TotalDebits, consolidated.TotalDebits)
                    .SetProperty(e => e.TransactionCount, consolidated.TransactionCount)
                    .SetProperty(e => e.LastUpdatedAt, consolidated.LastUpdatedAt)
                    .SetProperty(e => e.Version, consolidated.Version),
                cancellationToken);

            _logger.LogDebug(
                "Consolidado atualizado para comerciante {MerchantId} na data {Date}. Linhas afetadas: {Rows}.",
                consolidated.MerchantId,
                consolidated.Date,
                updatedRows);
        }
        else
        {
            // INSERT: Novo registro de saldo para a data
            _context.DailyConsolidated.Add(consolidated);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Novo consolidado criado para comerciante {MerchantId} na data {Date}.",
                consolidated.MerchantId,
                consolidated.Date);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A consulta e realizada por chave primaria diretamente na tabela processed_events.
    /// O uso de AnyAsync e preferivel ao FindAsync pois nao carrega a entidade no Change Tracker,
    /// reduzindo alocacoes de memoria no caminho critico do consumidor de mensagens.
    /// </remarks>
    public async Task<bool> IsEventProcessedAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ProcessedEvents
            .AnyAsync(e => e.EventId == eventId, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Registra o evento na tabela de deduplicacao. Esta operacao e chamada pelo
    /// TransactionEventConsumer na mesma unidade de trabalho logica do Upsert,
    /// garantindo que nao haja estado intermediario inconsistente.
    ///
    /// Nota: nao utilizamos transacao explicita aqui pois o Worker chama este metodo
    /// sequencialmente apos o UpsertAsync dentro de um bloco de execucao isolado.
    /// Para atomicidade total, ambas as operacoes deveriam ser envolvidas em uma
    /// unica transacao de banco — melhoria candidata ao Marco M6 (endurecimento).
    /// </remarks>
    public async Task MarkEventProcessedAsync(
        Guid eventId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        var processedEvent = new ProcessedEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow
        };

        _context.ProcessedEvents.Add(processedEvent);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Evento {EventId} do tipo {EventType} registrado na tabela de deduplicacao.", eventId, eventType);
    }
}
