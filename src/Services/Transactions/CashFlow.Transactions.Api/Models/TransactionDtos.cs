namespace CashFlow.Transactions.Api.Models;

/// <summary>
/// Modelo de transferencia de dados (DTO) para a requisicao de criacao de lancamento financeiro.
/// Representa o contrato publico JSON aceito pelo endpoint POST /api/v1/transactions.
/// </summary>
/// <param name="MerchantId">Identificador unico do comerciante titular do caixa financeiro (obrigatorio).</param>
/// <param name="Amount">Valor monetario da transacao financeira (deve ser estritamente maior que zero).</param>
/// <param name="Type">Classificacao contabil da operacao: 'Credit' (credito) ou 'Debit' (debito).</param>
/// <param name="Description">Historico descritivo ou detalhamento contabil da operacao.</param>
/// <param name="IdempotencyKey">Chave de idempotencia opcional para prevencao de duplicidade.</param>
public record RequisicaoLancamentoDto(
    string MerchantId,
    decimal Amount,
    string Type,
    string Description,
    string? IdempotencyKey = null
);

/// <summary>
/// Modelo de transferencia de dados (DTO) para a resposta de lancamento financeiro persistido.
/// Emitido com status HTTP 201 Created (ou HTTP 200 OK no caso de duplicatas idempotentes).
/// </summary>
/// <param name="TransactionId">UUID da transacao gravada ou recuperada por idempotencia.</param>
/// <param name="MerchantId">Identificador do comerciante proprietario da operacao.</param>
/// <param name="Amount">Valor monetario da operacao registrada.</param>
/// <param name="Type">Tipo contabil da operacao efetuada.</param>
/// <param name="CreatedAt">Data e hora UTC em que o lancamento foi registrado.</param>
public record RespostaLancamentoDto(
    Guid TransactionId,
    string MerchantId,
    decimal Amount,
    string Type,
    DateTime CreatedAt
);
