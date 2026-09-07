using Prometheus;

namespace CashFlow.Consolidated.Worker.Diagnostics;

/// <summary>
/// Metricas customizadas de operacao assincrona para o Worker de Consolidado.
/// </summary>
public static class WorkerMetrics
{
    /// <summary>
    /// Contador de eventos consumidos e processados pelo worker com resultado (success/duplicate/dlq).
    /// </summary>
    public static readonly Counter EventsProcessedTotal = Metrics.CreateCounter(
        "cashflow_worker_events_processed_total",
        "Total de eventos de lancamento processados pelo Worker de Consolidado.",
        new CounterConfiguration
        {
            LabelNames = new[] { "event_type", "status" }
        });

    /// <summary>
    /// Histograma de duracao em segundos do processamento de um evento pelo Worker.
    /// </summary>
    public static readonly Histogram EventProcessingDurationSeconds = Metrics.CreateHistogram(
        "cashflow_worker_event_duration_seconds",
        "Duracao em segundos do processamento de um evento de lancamento pelo Worker.",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(start: 0.001, factor: 2, count: 10)
        });
}
