namespace CashFlow.Shared.Domain.Events;

/// <summary>
/// Contrato basico para todos os eventos de dominio emitidos no ecossistema CashFlow.
/// Na arquitetura orientada a eventos (EDA), eventos de dominio representam fatos
/// imutaveis que ja ocorreram no passado e devem ser propagados para outros contextos.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// Identificador unico global do evento, utilizado para rastreamento distribuido
    /// e deduplicacao idempotente no consumidor consumidor de destino.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Carimbo de data e hora UTC em que o evento ocorreu no dominio de origem.
    /// </summary>
    DateTime OccurredOn { get; }
}

/// <summary>
/// Evento de dominio publicado quando uma nova transacao financeira (credito ou debito)
/// e registrada com sucesso no servico de lancamentos (Write-Side).
/// Este evento e serializado em JSON e transmitido assincronamente via RabbitMQ
/// para o Consolidated Worker, garantindo desacoplamento temporal e resiliencia.
/// </summary>
/// <param name="TransactionId">Identificador unico da transacao persistida no PostgreSQL.</param>
/// <param name="MerchantId">Identificador do comerciante proprietario do caixa financeiro.</param>
/// <param name="Amount">Valor monetario da transacao (deve ser estritamente positivo).</param>
/// <param name="Type">Tipo da transacao: 'Credit' (entrada de caixa) ou 'Debit' (saida de caixa).</param>
/// <param name="Description">Descricao detalhada ou historico do lancamento.</param>
/// <param name="CreatedAt">Momento exato da criacao do lancamento no banco relacional.</param>
public record TransactionCreatedEvent(
    Guid TransactionId,
    string MerchantId,
    decimal Amount,
    string Type,
    string Description,
    DateTime CreatedAt
) : IDomainEvent
{
    /// <summary>
    /// Identificador unico do evento, gerado automaticamente para assegurar idempotencia no worker.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Momento UTC da publicacao do evento.
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
