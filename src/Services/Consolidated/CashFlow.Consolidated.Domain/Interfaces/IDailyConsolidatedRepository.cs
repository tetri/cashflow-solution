using CashFlow.Consolidated.Domain.Entities;

namespace CashFlow.Consolidated.Domain.Interfaces;

/// <summary>
/// Contrato de repositorio responsavel pela persistencia e consulta de saldos diarios no PostgreSQL.
/// Inclui operacoes de busca, upsert atomico e controle estrito de idempotencia via tabela processed_events.
/// </summary>
public interface IDailyConsolidatedRepository
{
    /// <summary>
    /// Consulta o saldo consolidado de um comerciante para uma data especifica.
    /// </summary>
    /// <param name="merchantId">Identificador do comerciante.</param>
    /// <param name="date">Data de referencia.</param>
    /// <param name="cancellationToken">Token de cancelamento cooperativo.</param>
    /// <returns>O consolidado localizado ou nulo se nao houver movimentacao registrada.</returns>
    Task<DailyConsolidated?> GetAsync(string merchantId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insere ou atualiza atomicamente o consolidado diario no banco de dados.
    /// </summary>
    /// <param name="consolidated">Agregado de consolidado atualizado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task UpsertAsync(DailyConsolidated consolidated, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida se determinado evento de transacao ja foi processado previamente na tabela processed_events.
    /// Esta consulta garante o consumo estritamente idempotente (at-least-once sem duplicacao).
    /// </summary>
    /// <param name="eventId">GUID do evento de dominio.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Verdadeiro se o evento ja foi computado, falso caso contrario.</returns>
    Task<bool> IsEventProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra atomicamente o identificador do evento na tabela de deduplicacao processed_events.
    /// </summary>
    /// <param name="eventId">GUID do evento consumido.</param>
    /// <param name="eventType">Nome estrutural do tipo de evento.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task MarkEventProcessedAsync(Guid eventId, string eventType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstracao para a camada de cache distribuido em memoria (Redis).
/// Utilizada pela API de leitura (Read-Side) para atender consultas com latencia inferior a 5ms
/// e pelo worker de consolidacao para sincronizacao imediata no padrao Write-Through.
/// </summary>
public interface IConsolidatedCacheService
{
    /// <summary>
    /// Recupera o consolidado diario a partir da memoria RAM do Redis.
    /// </summary>
    /// <param name="merchantId">Identificador do comerciante.</param>
    /// <param name="date">Data do consolidado consultado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Instancia deserializada ou nulo sob cache miss ou indisponibilidade.</returns>
    Task<DailyConsolidated?> GetAsync(string merchantId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Armazena o consolidado no Redis configurando o tempo de expiracao (TTL) adequado.
    /// </summary>
    /// <param name="consolidated">Entidade a ser serializada e armazenada em cache.</param>
    /// <param name="ttl">Tempo de vida da chave (opcional; aplica politica padrao se nulo).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SetAsync(DailyConsolidated consolidated, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalida explicitamente a chave de consolidado em memoria do Redis.
    /// </summary>
    /// <param name="merchantId">Identificador do comerciante.</param>
    /// <param name="date">Data da chave a ser expurgada.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task RemoveAsync(string merchantId, DateOnly date, CancellationToken cancellationToken = default);
}
