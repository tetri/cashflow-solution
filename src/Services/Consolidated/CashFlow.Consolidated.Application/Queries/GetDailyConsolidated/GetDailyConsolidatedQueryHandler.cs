using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidated.Application.Queries.GetDailyConsolidated;

/// <summary>
/// Consulta CQRS para recuperacao do saldo consolidado de um comerciante por data.
/// </summary>
/// <param name="MerchantId">Identificador unico do comerciante.</param>
/// <param name="Date">Data contábil de interesse.</param>
public record GetDailyConsolidatedQuery(string MerchantId, DateOnly Date);

/// <summary>
/// Objeto de transferencia de dados (DTO) otimizado para consumo HTTP da API de Consolidado.
/// Contem o cabecalho de auditoria indicando se a resposta foi atendida via Redis ou PostgreSQL.
/// </summary>
/// <param name="MerchantId">Identificador do comerciante.</param>
/// <param name="Date">Data do consolidado.</param>
/// <param name="TotalCredits">Soma de entradas registradas.</param>
/// <param name="TotalDebits">Soma de saidas registradas.</param>
/// <param name="ClosingBalance">Saldo liquido acumulado.</param>
/// <param name="TransactionCount">Quantidade de transacoes que compuseram o saldo.</param>
/// <param name="LastUpdatedAt">Data/hora UTC da ultima atualizacao.</param>
/// <param name="Cached">Booleano indicando se a consulta foi servida do cache em memoria.</param>
public record DailyConsolidatedDto(
    string MerchantId,
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal ClosingBalance,
    int TransactionCount,
    DateTime LastUpdatedAt,
    bool Cached
);

/// <summary>
/// Manipulador de consulta (QueryHandler) responsavel por atender consultas de saldo consolidado.
/// Implementa a estrategia Cache-Aside com alta performance:
/// 1. Busca prioritariamente no cache Redis (latencia sub-5ms, capacidade superior a 50 RPS).
/// 2. Sob cache miss ou falha no Redis, degrada suavemente para o PostgreSQL.
/// 3. Popula o cache com o valor recuperado para atender requisicoes subsequentes.
/// </summary>
public class GetDailyConsolidatedQueryHandler
{
    private readonly IConsolidatedCacheService _cacheService;
    private readonly IDailyConsolidatedRepository _repository;
    private readonly ILogger<GetDailyConsolidatedQueryHandler> _logger;

    /// <summary>
    /// Inicializa o manipulador de consulta com servico de cache, repositorio e logger.
    /// </summary>
    /// <param name="cacheService">Servico de caching Redis.</param>
    /// <param name="repository">Repositorio de persistencia relacional PostgreSQL.</param>
    /// <param name="logger">Mecanismo de telemetria.</param>
    public GetDailyConsolidatedQueryHandler(
        IConsolidatedCacheService cacheService,
        IDailyConsolidatedRepository repository,
        ILogger<GetDailyConsolidatedQueryHandler> logger)
    {
        _cacheService = cacheService;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Processa a consulta de consolidado com garantia de fallback resiliente.
    /// </summary>
    /// <param name="query">Parametros de comerciante e data.</param>
    /// <param name="cancellationToken">Token de cancelamento cooperativo.</param>
    /// <returns>DTO contendo o saldo diario consolidado.</returns>
    public async Task<DailyConsolidatedDto> HandleAsync(
        GetDailyConsolidatedQuery query,
        CancellationToken cancellationToken = default)
    {
        // 1. Tentar ler do Cache Distribuido (Redis) - Otimizado para > 50 req/s (< 5ms)
        var cached = await _cacheService.GetAsync(query.MerchantId, query.Date, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug("Cache Hit para consolidado do comerciante {MerchantId} em {Date}", query.MerchantId, query.Date);
            return ToDto(cached, isCached: true);
        }

        // 2. Cache Miss: Buscar no PostgreSQL
        _logger.LogInformation("Cache Miss para consolidado do comerciante {MerchantId} em {Date}. Consultando banco relacional.", query.MerchantId, query.Date);
        var consolidated = await _repository.GetAsync(query.MerchantId, query.Date, cancellationToken);

        if (consolidated is null)
        {
            // Instancia modelo contabil zerado para datas sem nenhuma movimentacao
            consolidated = new DailyConsolidated(query.MerchantId, query.Date);
        }

        // 3. Preencher o cache Redis para acelerar as proximas consultas
        await _cacheService.SetAsync(consolidated, cancellationToken: cancellationToken);

        return ToDto(consolidated, isCached: false);
    }

    private static DailyConsolidatedDto ToDto(DailyConsolidated entity, bool isCached) =>
        new(
            entity.MerchantId,
            entity.Date,
            entity.TotalCredits,
            entity.TotalDebits,
            entity.ClosingBalance,
            entity.TransactionCount,
            entity.LastUpdatedAt,
            isCached
        );
}
