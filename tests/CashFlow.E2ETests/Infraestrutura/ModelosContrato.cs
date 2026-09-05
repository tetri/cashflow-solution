using System.Text.Json.Serialization;

namespace CashFlow.E2ETests.Infraestrutura;

/// <summary>
/// Modelo de requisicao para o endpoint de registro de lancamentos financeiros.
/// Segue rigorosamente o contrato definido na documentacao de arquitetura.
/// </summary>
public record RequisicaoLancamento(
    [property: JsonPropertyName("merchantId")] string MerchantId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey = null
);

/// <summary>
/// Modelo de resposta retornado apos o registro com sucesso de um lancamento financeiro.
/// </summary>
public record RespostaLancamento(
    [property: JsonPropertyName("transactionId")] Guid TransactionId,
    [property: JsonPropertyName("merchantId")] string MerchantId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt
);

/// <summary>
/// Modelo de resposta da consulta de consolidado diario de fluxo de caixa.
/// </summary>
public record RespostaConsolidado(
    [property: JsonPropertyName("merchantId")] string MerchantId,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("totalCredits")] decimal TotalCredits,
    [property: JsonPropertyName("totalDebits")] decimal TotalDebits,
    [property: JsonPropertyName("closingBalance")] decimal ClosingBalance,
    [property: JsonPropertyName("transactionCount")] int TransactionCount,
    [property: JsonPropertyName("lastUpdatedAt")] DateTime LastUpdatedAt,
    [property: JsonPropertyName("cached")] bool Cached
);

/// <summary>
/// Modelo padrao RFC 7231 ProblemDetails para respostas de erro e validacao da API.
/// </summary>
public record DetalhesProblemaRfc7231(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("detail")] string Detail
);

/// <summary>
/// Contrato de evento de dominio TransactionCreatedEvent para mensageria assincrona.
/// </summary>
public record EventoLancamentoCriado(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("transactionId")] Guid TransactionId,
    [property: JsonPropertyName("merchantId")] string MerchantId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("occurredOn")] DateTime OccurredOn
);
