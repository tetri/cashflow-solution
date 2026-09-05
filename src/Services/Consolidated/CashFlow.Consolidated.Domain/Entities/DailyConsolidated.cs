namespace CashFlow.Consolidated.Domain.Entities;

/// <summary>
/// Entidade raiz de agregacao responsavel pelo balanco consolidado diario de um comerciante.
/// No modelo CQRS (Read-Side), este agregado centraliza a acumulacao de creditos e debitos,
/// mantendo o saldo liquido e a contagem de transacoes sincronizados de forma transacional.
/// A propriedade Version e utilizada para garantir concorrencia otimista na persistencia.
/// </summary>
public class DailyConsolidated
{
    /// <summary>
    /// Identificador do comerciante proprietario do fluxo de caixa.
    /// </summary>
    public string MerchantId { get; private set; } = null!;

    /// <summary>
    /// Data contábil de consolidacao dos valores (formato yyyy-MM-dd).
    /// </summary>
    public DateOnly Date { get; private set; }

    /// <summary>
    /// Somatorio acumulado de todos os lancamentos do tipo Credito ocorridos na data.
    /// </summary>
    public decimal TotalCredits { get; private set; }

    /// <summary>
    /// Somatorio acumulado de todos os lancamentos do tipo Debito ocorridos na data.
    /// </summary>
    public decimal TotalDebits { get; private set; }

    /// <summary>
    /// Saldo liquido apurado no encerramento diario (TotalCredits - TotalDebits).
    /// Calculado dinamicamente para preservar consistencia matematica pura sem redundancia.
    /// </summary>
    public decimal ClosingBalance => TotalCredits - TotalDebits;

    /// <summary>
    /// Quantidade total de transacoes agregadas no dia.
    /// </summary>
    public int TransactionCount { get; private set; }

    /// <summary>
    /// Carimbo de data/hora UTC da ultima atualizacao de saldo realizada.
    /// </summary>
    public DateTime LastUpdatedAt { get; private set; }

    /// <summary>
    /// Versao do registro contábil para suporte a controle de concorrencia otimista.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Construtor sem parametros exigido para mecanismos de hidratacao do banco de dados (EF Core).
    /// </summary>
    private DailyConsolidated()
    {
    }

    /// <summary>
    /// Construtor completo anotado para serializacao e desserializacao JSON (ex: Redis cache).
    /// </summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public DailyConsolidated(
        string merchantId,
        DateOnly date,
        decimal totalCredits,
        decimal totalDebits,
        int transactionCount,
        DateTime lastUpdatedAt,
        int version)
    {
        MerchantId = merchantId;
        Date = date;
        TotalCredits = totalCredits;
        TotalDebits = totalDebits;
        TransactionCount = transactionCount;
        LastUpdatedAt = lastUpdatedAt;
        Version = version;
    }

    /// <summary>
    /// Inicializa um novo registro de consolidado diario zerado para uma data de negocio.
    /// </summary>
    /// <param name="merchantId">Identificador do comerciante.</param>
    /// <param name="date">Data da consolidacao contábil.</param>
    public DailyConsolidated(string merchantId, DateOnly date)
    {
        MerchantId = merchantId;
        Date = date;
        TotalCredits = 0;
        TotalDebits = 0;
        TransactionCount = 0;
        LastUpdatedAt = DateTime.UtcNow;
        Version = 1;
    }

    /// <summary>
    /// Aplica uma transacao financeira ao consolidado, recalculando atomicamente o saldo
    /// e incrementando o numero de versoes e transacoes registradas.
    /// </summary>
    /// <param name="amount">Valor financeiro positivo a ser agregado.</param>
    /// <param name="type">Natureza contabil: 'Credit' ou 'Debit'.</param>
    /// <exception cref="ArgumentException">Lancada caso o tipo seja diferente de Credit ou Debit.</exception>
    public void ApplyTransaction(decimal amount, string type)
    {
        if (type.Equals("Credit", StringComparison.OrdinalIgnoreCase))
        {
            TotalCredits += amount;
        }
        else if (type.Equals("Debit", StringComparison.OrdinalIgnoreCase))
        {
            TotalDebits += amount;
        }
        else
        {
            throw new ArgumentException($"Tipo de transacao desconhecido: '{type}'. Tipos validos sao 'Credit' e 'Debit'.", nameof(type));
        }

        TransactionCount++;
        LastUpdatedAt = DateTime.UtcNow;
        Version++;
    }
}
