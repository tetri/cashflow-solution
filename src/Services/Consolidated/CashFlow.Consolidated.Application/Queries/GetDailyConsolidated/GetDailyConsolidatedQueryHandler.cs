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
/// Implementa a estrategia Cache-Aside com alta performance e protecao avancada contra Cache Stampede:
/// 1. Busca prioritariamente no cache Redis (latencia sub-5ms, capacidade superior a 50 RPS).
/// 2. Sob cache miss, utiliza sincronizacao controlada (Double-Checked Locking com SemaphoreSlim)
///    para assegurar que apenas uma unica requisicao consulte o PostgreSQL, enquanto as demais
///    aguardam para receber o dado ja reidratado no cache em memoria.
/// 3. Degrada suavemente para o PostgreSQL caso o Redis esteja indisponivel.
/// </summary>
public class GetDailyConsolidatedQueryHandler
{
    private readonly IConsolidatedCacheService _cacheService;
    private readonly IDailyConsolidatedRepository _repository;
    private readonly ILogger<GetDailyConsolidatedQueryHandler> _logger;

    // Semaforo estatico para controle de concorrencia local e mitigacao de Cache Stampede
    private static readonly SemaphoreSlim TravaSincronizacaoCache = new SemaphoreSlim(1, 1);

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
    /// Processa a consulta de consolidado com garantia de fallback resiliente e protecao contra efeito manada.
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

        // 2. Protecao contra Cache Stampede (Efeito Manada):
        // Quando ocorrem dezenas de requisicoes simultaneas para a mesma chave expirada,
        // apenas uma adquire o semaforo e vai ao banco relacional.
        await TravaSincronizacaoCache.WaitAsync(cancellationToken);
        try
        {
            // Double-Check: verifica se outra requisicao concorrente acabou de preencher o cache enquanto aguardavamos
            var cachedAposTrava = await _cacheService.GetAsync(query.MerchantId, query.Date, cancellationToken);
            if (cachedAposTrava is not null)
            {
                _logger.LogDebug("Cache Hit apos sincronizacao (protecao contra stampede) para {MerchantId} em {Date}", query.MerchantId, query.Date);
                return ToDto(cachedAposTrava, isCached: true);
            }

            // 3. Cache Miss confirmado: Consulta unica ao PostgreSQL
            _logger.LogInformation("Cache Miss para consolidado do comerciante {MerchantId} em {Date}. Consultando banco relacional.", query.MerchantId, query.Date);
            var consolidated = await _repository.GetAsync(query.MerchantId, query.Date, cancellationToken);

            if (consolidated is null)
            {
                // Instancia modelo contabil zerado para datas sem nenhuma movimentacao
                consolidated = new DailyConsolidated(query.MerchantId, query.Date);
            }

            // 4. Preencher o cache Redis para acelerar as proximas consultas
            await _cacheService.SetAsync(consolidated, cancellationToken: cancellationToken);

            return ToDto(consolidated, isCached: false);
        }
        finally
        {
            TravaSincronizacaoCache.Release();
        }
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
