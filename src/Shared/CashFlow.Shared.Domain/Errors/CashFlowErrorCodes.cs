namespace CashFlow.Shared.Domain.Errors;

/// <summary>
/// Catalogo centralizado de codigos de erro unicos, estaveis e legiveis por maquina.
/// Em total conformidade com as diretrizes do OWASP API Security (CWE-209 / Information Exposure)
/// e o padrao RFC 7807 / RFC 9457 (Problem Details for HTTP APIs).
/// Permite que clientes e sistemas consumidores tratem falhas programaticamente sem depender de mensagens textuais.
/// </summary>
public static class CashFlowErrorCodes
{
    // =========================================================================
    // Seguranca, Autenticacao e Autorizacao (OWASP API1 / API2)
    // =========================================================================

    /// <summary>
    /// Requisicao rejeitada por ausencia ou invalidade de credenciais de autenticacao (X-Api-Key ou Bearer Token).
    /// </summary>
    public const string AutenticacaoNaoAutorizada = "CF_AUTH_001";

    /// <summary>
    /// Credencial valida, porem sem permissao para acessar ou manipular o recurso do comerciante especificado.
    /// </summary>
    public const string AutenticacaoAcessoNegado = "CF_AUTH_002";

    // =========================================================================
    // Servico de Lancamentos (Write-Side - Transacoes)
    // =========================================================================

    /// <summary>
    /// Caracteres invalidos, emojis ou parametros malformados detectados na requisicao de lancamento.
    /// </summary>
    public const string TransacaoParametroInvalido = "CF_TX_001";

    /// <summary>
    /// Identificador do comerciante ausente, nulo ou excedendo o limite permitido de caracteres.
    /// </summary>
    public const string TransacaoComercianteInvalido = "CF_TX_002";

    /// <summary>
    /// Valor da transacao invalido (deve ser estritamente maior que zero).
    /// </summary>
    public const string TransacaoValorInvalido = "CF_TX_003";

    /// <summary>
    /// Descricao do lancamento ausente ou excedendo o tamanho maximo permitido.
    /// </summary>
    public const string TransacaoDescricaoInvalida = "CF_TX_004";

    /// <summary>
    /// Tipo de operacao financeira desconhecido ou divergente dos permitidos ('Credit' ou 'Debit').
    /// </summary>
    public const string TransacaoTipoInvalido = "CF_TX_005";

    /// <summary>
    /// Conflito de integridade: a chave de idempotencia informada ja foi utilizada para outra transacao com payload distinto.
    /// </summary>
    public const string TransacaoIdempotenciaConflito = "CF_TX_006";

    /// <summary>
    /// Transacao financeira solicitada nao foi localizada na base de dados relacional.
    /// </summary>
    public const string TransacaoNaoEncontrada = "CF_TX_007";

    // =========================================================================
    // Servico de Consolidado Diario (Read-Side - Consultas)
    // =========================================================================

    /// <summary>
    /// Parametros de rota ou consulta invalidos, nulos ou contendo caracteres proibidos na API de consolidado.
    /// </summary>
    public const string ConsolidadoParametroInvalido = "CF_CONS_001";

    /// <summary>
    /// Formato da data contabil incompativel com o padrao ISO 8601 estrito (yyyy-MM-dd).
    /// </summary>
    public const string ConsolidadoDataInvalida = "CF_CONS_002";

    /// <summary>
    /// Registro de consolidado diario nao encontrado para o comerciante e data especificados.
    /// </summary>
    public const string ConsolidadoNaoEncontrado = "CF_CONS_003";

    // =========================================================================
    // Falhas de Sistema e Infraestrutura
    // =========================================================================

    /// <summary>
    /// Erro interno nao tratado ou falha de infraestrutura capturada sem exposicao de stack trace.
    /// </summary>
    public const string SistemaErroInesperado = "CF_SYS_001";
}
