using Prometheus;

namespace CashFlow.Transactions.Api.Diagnostics;

/// <summary>
/// Metricas customizadas de negocio e infraestrutura para a API de Lancamentos.
/// </summary>
public static class TransactionsMetrics
{
    /// <summary>
    /// Contador de transacoes criadas rotuladas por tipo (Credit/Debit) e resultado (success/conflict/error).
    /// </summary>
    public static readonly Counter TransactionsCreatedTotal = Metrics.CreateCounter(
        "cashflow_transactions_created_total",
        "Total de transacoes financeiras processadas pela API de Lancamentos.",
        new CounterConfiguration
        {
            LabelNames = new[] { "type", "status" }
        });

    /// <summary>
    /// Volume financeiro acumulado das transacoes processadas rotulado por tipo (Credit/Debit).
    /// </summary>
    public static readonly Counter TransactionAmountTotal = Metrics.CreateCounter(
        "cashflow_transaction_amount_total",
        "Volume financeiro acumulado das transacoes processadas.",
        new CounterConfiguration
        {
            LabelNames = new[] { "type" }
        });
}
