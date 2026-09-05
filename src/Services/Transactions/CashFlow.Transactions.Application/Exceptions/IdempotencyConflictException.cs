namespace CashFlow.Transactions.Application.Exceptions;

/// <summary>
/// Excecao de conflito de negocio disparada quando uma chave de idempotencia informada
/// ja foi previamente utilizada para registrar uma transacao com parametros divergentes.
/// De acordo com o protocolo HTTP REST e a semantica de idempotencia, a reutilizacao de uma chave
/// com carga util diferente constitui violacao de integridade e deve retornar status HTTP 409 Conflict.
/// </summary>
public class IdempotencyConflictException : Exception
{
    /// <summary>
    /// Inicializa uma nova instancia da excecao com a mensagem padrao de conflito de idempotencia.
    /// </summary>
    public IdempotencyConflictException()
        : base("A chave de idempotencia fornecida ja foi utilizada com dados divergentes.")
    {
    }

    /// <summary>
    /// Inicializa uma nova instancia da excecao com mensagem personalizada.
    /// </summary>
    /// <param name="message">Mensagem descritiva da divergencia encontrada.</param>
    public IdempotencyConflictException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Inicializa uma nova instancia da excecao com mensagem e causa raiz associada.
    /// </summary>
    /// <param name="message">Mensagem de erro explicativa.</param>
    /// <param name="innerException">Excecao interna que motivou o conflito.</param>
    public IdempotencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
