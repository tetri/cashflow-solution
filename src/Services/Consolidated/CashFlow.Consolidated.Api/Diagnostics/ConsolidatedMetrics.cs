using Prometheus;

namespace CashFlow.Consolidated.Api.Diagnostics;

/// <summary>
/// Metricas customizadas de negocio e cache para a API de Consolidado Diario.
/// </summary>
public static class ConsolidatedMetrics
{
    /// <summary>
    /// Contador de consultas ao consolidado rotuladas por cache hit (cached=true/false).
    /// </summary>
    public static readonly Counter ConsolidatedQueriesTotal = Metrics.CreateCounter(
        "cashflow_consolidated_queries_total",
        "Total de consultas ao saldo consolidado diario processadas pela API.",
        new CounterConfiguration
        {
            LabelNames = new[] { "cached" }
        });

    /// <summary>
    /// Total de consultas atendidas diretamente na memoria do Redis (latencia sub-5ms).
    /// </summary>
    public static readonly Counter CacheHitsTotal = Metrics.CreateCounter(
        "cashflow_cache_hits_total",
        "Total de consultas atendidas com exito pelo cache distribuido Redis.");

    /// <summary>
    /// Total de consultas que resultaram em cache miss e demandaram consulta ao PostgreSQL.
    /// </summary>
    public static readonly Counter CacheMissesTotal = Metrics.CreateCounter(
        "cashflow_cache_misses_total",
        "Total de consultas que resultaram em cache miss e recorreram ao banco relacional.");
}
