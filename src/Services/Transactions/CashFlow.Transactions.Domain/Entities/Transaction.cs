namespace CashFlow.Transactions.Domain.Entities;

/// <summary>
/// Especifica a natureza contabil da transacao de fluxo de caixa.
/// Credito representa entrada de recursos e Debito representa saida de recursos.
/// </summary>
public enum TransactionType
{
    /// <summary>
    /// Entrada de valores no caixa do comerciante.
    /// </summary>
    Credit = 1,

    /// <summary>
    /// Saida de valores do caixa do comerciante.
    /// </summary>
    Debit = 2
}

/// <summary>
/// Entidade raiz de agregacao que representa um lancamento financeiro no dominio de transacoes.
/// Esta classe segue o principio de encapsulamento estrito do DDD: seu estado e imutavel
/// externamente (setters privados) e sua criacao so e permitida atraves de um construtor
/// que valida rigorosamente todas as invariantes de negocio antes da persistencia.
/// </summary>
public class Transaction
{
    /// <summary>
    /// Identificador unico universal (UUID) da transacao.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Identificador do comerciante responsavel pela operacao.
    /// </summary>
    public string MerchantId { get; private set; } = null!;

    /// <summary>
    /// Valor monetario do lancamento. Deve ser estritamente maior que zero.
    /// </summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// Classificacao contabil (Credito ou Debito).
    /// </summary>
    public TransactionType Type { get; private set; }

    /// <summary>
    /// Detalhamento textual ou historico comercial do lancamento.
    /// </summary>
    public string Description { get; private set; } = null!;

    /// <summary>
    /// Data e hora UTC do momento em que a transacao foi registrada.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Construtor sem parametros exigido pelo mecanismo de mapeamento objeto-relacional (EF Core).
    /// Declarado como privado para impedir instanciacao indevida pela aplicacao sem validacao.
    /// </summary>
    private Transaction()
    {
    }

    /// <summary>
    /// Inicializa uma nova instancia da entidade Transaction validando todas as invariantes.
    /// </summary>
    /// <param name="merchantId">Identificador valido do comerciante.</param>
    /// <param name="amount">Valor da operacao (estritamente positivo).</param>
    /// <param name="type">Tipo contabil da operacao.</param>
    /// <param name="description">Descricao informativa do lancamento.</param>
    /// <exception cref="ArgumentException">Lancada quando qualquer invariante for violada.</exception>
    public Transaction(string merchantId, decimal amount, TransactionType type, string description)
    {
        // Validacao de invariante: MerchantId nao pode ser nulo ou espaco em branco
        if (string.IsNullOrWhiteSpace(merchantId))
        {
            throw new ArgumentException("O identificador do comerciante (MerchantId) e obrigatorio.", nameof(merchantId));
        }

        // Validacao de invariante: o valor da transacao deve ser estritamente positivo
        if (amount <= 0)
        {
            throw new ArgumentException("O valor da transacao deve ser estritamente maior que zero.", nameof(amount));
        }

        // Validacao de invariante: a descricao comercial e obrigatoria para auditoria financeira
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("A descricao da transacao e obrigatoria.", nameof(description));
        }

        Id = Guid.NewGuid();
        MerchantId = merchantId;
        Amount = amount;
        Type = type;
        Description = description;
        CreatedAt = DateTime.UtcNow;
    }
}
