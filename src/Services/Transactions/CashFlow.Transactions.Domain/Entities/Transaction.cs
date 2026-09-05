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
/// Entidade raiz de agregacao (Aggregate Root) que representa um lancamento financeiro no dominio de transacoes.
/// Esta classe segue o principio de encapsulamento estrito do DDD (Domain-Driven Design):
/// seu estado e imutavel externamente (setters privados) e sua criacao so e permitida atraves
/// de construtor validado ou metodos de fabrica expressivos que asseguram todas as invariantes de negocio.
/// </summary>
public class Transaction
{
    /// <summary>
    /// Identificador unico universal (UUID) da transacao gerado no dominio.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Identificador do comerciante proprietario do caixa financeiro (limite maximo de 50 caracteres).
    /// </summary>
    public string MerchantId { get; private set; } = null!;

    /// <summary>
    /// Valor monetario do lancamento. Deve ser estritamente maior que zero para garantir consistencia contavel.
    /// </summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// Classificacao contabil da operacao (Credito ou Debito).
    /// </summary>
    public TransactionType Type { get; private set; }

    /// <summary>
    /// Detalhamento textual ou historico comercial do lancamento (limite maximo de 500 caracteres).
    /// </summary>
    public string Description { get; private set; } = null!;

    /// <summary>
    /// Carimbo de data e hora UTC do momento em que o lancamento foi concebido e registrado.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Chave opcional de idempotencia utilizada para prevenir o processamento duplicado de uma mesma requisicao.
    /// Quando informada, possui limite de ate 128 caracteres e normalizacao de espacos em branco.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>
    /// Construtor sem parametros exigido pelo mecanismo de mapeamento objeto-relacional (EF Core).
    /// Declarado com visibilidade privada para impedir instanciacao indevida pela aplicacao sem validacao.
    /// </summary>
    private Transaction()
    {
    }

    /// <summary>
    /// Inicializa uma nova instancia da entidade Transaction validando rigorosamente todas as invariantes de dominio.
    /// </summary>
    /// <param name="merchantId">Identificador valido do comerciante (obrigatorio, maximo 50 caracteres).</param>
    /// <param name="amount">Valor monetario da operacao (deve ser estritamente maior que zero).</param>
    /// <param name="type">Tipo contabil da transacao (Credito ou Debito definido no enum).</param>
    /// <param name="description">Descricao ou historico do lancamento (obrigatorio, maximo 500 caracteres).</param>
    /// <param name="idempotencyKey">Chave de idempotencia opcional para prevencao de duplicidade (maximo 128 caracteres).</param>
    /// <exception cref="ArgumentException">Lancada quando qualquer invariante de negocio for violada.</exception>
    public Transaction(
        string merchantId,
        decimal amount,
        TransactionType type,
        string description,
        string? idempotencyKey = null)
    {
        // Invariante 1: O identificador do comerciante e obrigatorio e nao pode ser vazio
        if (string.IsNullOrWhiteSpace(merchantId))
        {
            throw new ArgumentException("O identificador do comerciante (MerchantId) e obrigatorio.", nameof(merchantId));
        }

        // Invariante 2: O identificador do comerciante nao pode ultrapassar 50 caracteres
        if (merchantId.Trim().Length > 50)
        {
            throw new ArgumentException("O identificador do comerciante nao pode exceder 50 caracteres.", nameof(merchantId));
        }

        // Invariante 3: O valor monetario deve ser estritamente maior que zero (lancamentos nulos ou negativos sao invalidos)
        if (amount <= 0)
        {
            throw new ArgumentException("O valor da transacao deve ser estritamente maior que zero.", nameof(amount));
        }

        // Invariante 4: O tipo da transacao deve pertencer explicitamente ao enum TransactionType (Credit ou Debit)
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentException("O tipo de transacao informado e invalido. Aceita-se apenas Credito ou Debito.", nameof(type));
        }

        // Invariante 5: A descricao do lancamento e obrigatoria para auditoria e rastreabilidade contabil
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("A descricao da transacao e obrigatoria.", nameof(description));
        }

        // Invariante 6: A descricao nao pode ultrapassar o limite maximo de 500 caracteres
        if (description.Trim().Length > 500)
        {
            throw new ArgumentException("A descricao da transacao nao pode exceder 500 caracteres.", nameof(description));
        }

        // Invariante 7: Normalizacao e validacao da chave de idempotencia opcional
        string? chaveNormalizada = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (chaveNormalizada is not null && chaveNormalizada.Length > 128)
        {
            throw new ArgumentException("A chave de idempotencia nao pode exceder 128 caracteres.", nameof(idempotencyKey));
        }

        Id = Guid.NewGuid();
        MerchantId = merchantId.Trim();
        Amount = amount;
        Type = type;
        Description = description.Trim();
        IdempotencyKey = chaveNormalizada;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Metodo de fabrica expressivo para criacao semantica de lancamento de Credito (entrada de recursos).
    /// </summary>
    /// <param name="merchantId">Identificador do comerciante.</param>
    /// <param name="amount">Valor financeiro positivo da entrada.</param>
    /// <param name="description">Descricao detalhada da operacao de credito.</param>
    /// <param name="idempotencyKey">Chave opcional para idempotencia da operacao.</param>
    /// <returns>Nova instancia validada da entidade Transaction configurada como Credito.</returns>
    public static Transaction CreateCredit(
        string merchantId,
        decimal amount,
        string description,
        string? idempotencyKey = null)
    {
        return new Transaction(merchantId, amount, TransactionType.Credit, description, idempotencyKey);
    }

    /// <summary>
    /// Metodo de fabrica expressivo para criacao semantica de lancamento de Debito (saida de recursos).
    /// </summary>
    /// <param name="merchantId">Identificador do comerciante.</param>
    /// <param name="amount">Valor financeiro positivo da saida.</param>
    /// <param name="description">Descricao detalhada da operacao de debito.</param>
    /// <param name="idempotencyKey">Chave opcional para idempotencia da operacao.</param>
    /// <returns>Nova instancia validada da entidade Transaction configurada como Debito.</returns>
    public static Transaction CreateDebit(
        string merchantId,
        decimal amount,
        string description,
        string? idempotencyKey = null)
    {
        return new Transaction(merchantId, amount, TransactionType.Debit, description, idempotencyKey);
    }
}
