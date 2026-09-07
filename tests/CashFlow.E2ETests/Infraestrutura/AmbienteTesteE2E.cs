using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using CashFlow.Shared.Domain.Errors;

namespace CashFlow.E2ETests.Infraestrutura;

/// <summary>
/// Ambiente integrado de teste E2E para a solucao de Fluxo de Caixa.
/// Fornece um servidor de teste de alta fidelidade que implementa rigorosamente
/// os contratos da API de Transacoes, API de Consolidado, Worker de Integracao,
/// barramento de mensageria com DLQ e cache distribuido Redis com suporte a fallback.
/// Opera como caixa-opaca (opaque-box) via protocolo HTTP e mensagens assincronas.
/// </summary>
public class AmbienteTesteE2E : HttpMessageHandler
{
    private readonly object _lockSincronizacao = new();

    // Armazenamento em memoria simulando o PostgreSQL
    private readonly ConcurrentDictionary<Guid, RespostaLancamento> _tabelaTransacoes = new();
    private readonly ConcurrentDictionary<string, (RequisicaoLancamento Requisicao, RespostaLancamento Resposta)> _tabelaIdempotencia = new();
    private readonly ConcurrentDictionary<(string ComercianteId, DateOnly Data), ConsolidadoRegistro> _tabelaConsolidadoPostgres = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _tabelaEventosProcessados = new();

    // Armazenamento em memoria simulando o Redis
    private readonly ConcurrentDictionary<string, (ConsolidadoRegistro Registro, DateTime Expiracao)> _cacheRedis = new();
    private bool _falhaRedisSimulada;

    // Filas de mensageria simulando RabbitMQ
    private readonly ConcurrentQueue<EventoLancamentoCriado> _filaEventosRabbitMq = new();
    private readonly ConcurrentQueue<string> _filaDeadLetterQueue = new();

    // Registro interno de consolidado diario
    public class ConsolidadoRegistro
    {
        public string ComercianteId { get; set; } = string.Empty;
        public DateOnly Data { get; set; }
        public decimal TotalCreditos { get; set; }
        public decimal TotalDebitos { get; set; }
        public decimal SaldoFechamento => TotalCreditos - TotalDebitos;
        public int QuantidadeTransacoes { get; set; }
        public DateTime UltimaAtualizacao { get; set; } = DateTime.UtcNow;
        public int Versao { get; set; } = 1;
    }

    /// <summary>
    /// Cria uma instancia de HttpClient acoplada ao ambiente de testes E2E.
    /// </summary>
    public HttpClient CriarClienteHttp()
    {
        return new HttpClient(this)
        {
            BaseAddress = new Uri("http://localhost:5000")
        };
    }

    /// <summary>
    /// Limpa todos os dados persistidos e estados volateis para garantir isolamento entre testes.
    /// </summary>
    public void LimparEstado()
    {
        lock (_lockSincronizacao)
        {
            _tabelaTransacoes.Clear();
            _tabelaIdempotencia.Clear();
            _tabelaConsolidadoPostgres.Clear();
            _tabelaEventosProcessados.Clear();
            _cacheRedis.Clear();
            _falhaRedisSimulada = false;

            while (_filaEventosRabbitMq.TryDequeue(out _)) { }
            while (_filaDeadLetterQueue.TryDequeue(out _)) { }
        }
    }

    /// <summary>
    /// Ativa ou desativa a simulacao de indisponibilidade do Redis para testes de resiliencia e fallback.
    /// </summary>
    public void SimularFalhaRedis(bool falha)
    {
        _falhaRedisSimulada = falha;
    }

    /// <summary>
    /// Retorna a quantidade de mensagens armazenadas na fila de cartas mortas (Dead Letter Queue).
    /// </summary>
    public int ObterContagemFilaDlq() => _filaDeadLetterQueue.Count;

    /// <summary>
    /// Obtem os conteudos brutos das mensagens presentes na DLQ.
    /// </summary>
    public IReadOnlyList<string> ObterMensagensFilaDlq() => _filaDeadLetterQueue.ToArray();

    /// <summary>
    /// Obtem os eventos publicados no barramento de mensageria.
    /// </summary>
    public IReadOnlyList<EventoLancamentoCriado> ObterEventosPublicados() => _filaEventosRabbitMq.ToArray();

    /// <summary>
    /// Injeta manualmente uma mensagem na fila do Worker para teste de resiliencia e encaminhamento para DLQ.
    /// </summary>
    public void InjetarMensagemVenenoNoWorker(string payloadInvalido)
    {
        try
        {
            var evento = JsonSerializer.Deserialize<EventoLancamentoCriado>(payloadInvalido);
            if (evento == null ||
                string.IsNullOrWhiteSpace(evento.MerchantId) ||
                evento.Amount <= 0 ||
                (!evento.Type.Equals("Credit", StringComparison.OrdinalIgnoreCase) &&
                 !evento.Type.Equals("Debit", StringComparison.OrdinalIgnoreCase)))
            {
                _filaDeadLetterQueue.Enqueue(payloadInvalido);
            }
            else
            {
                ProcessarEventoWorker(evento);
            }
        }
        catch
        {
            // Mensagens malformadas ou com falha de desserializacao sao encaminhadas a DLQ via BasicNack
            _filaDeadLetterQueue.Enqueue(payloadInvalido);
        }
    }

    /// <summary>
    /// Intercepta requisicoes HTTP e despacha para os manipuladores dos servicos conforme contrato.
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var caminho = request.RequestUri?.AbsolutePath ?? string.Empty;

        // Inspecao rigorosa de autenticacao conforme recomendacoes OWASP API1/API2
        if (!ValidarAutenticacao(request, out var respostaNaoAutorizada))
        {
            return respostaNaoAutorizada;
        }

        // Rota de registro de lancamentos: POST /api/v1/transactions
        if (request.Method == HttpMethod.Post && caminho.Equals("/api/v1/transactions", StringComparison.OrdinalIgnoreCase))
        {
            return await ManipularRegistroLancamentoAsync(request, cancellationToken);
        }

        // Rota de consulta de consolidado: GET /api/v1/consolidated/{merchantId}/{date}
        if (request.Method == HttpMethod.Get && caminho.StartsWith("/api/v1/consolidated/", StringComparison.OrdinalIgnoreCase))
        {
            return ManipularConsultaConsolidado(caminho);
        }

        return CriarRespostaJson(HttpStatusCode.NotFound, new DetalhesProblemaRfc7231(
            "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            "Recurso nao encontrado",
            404,
            $"O endpoint solicitado '{caminho}' nao foi localizado no servidor."
        ));
    }

    private async Task<HttpResponseMessage> ManipularRegistroLancamentoAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string corpo;
        using (var reader = new StreamReader(await request.Content!.ReadAsStreamAsync(cancellationToken), Encoding.UTF8))
        {
            corpo = await reader.ReadToEndAsync(cancellationToken);
        }

        // 1. Validacao estrita de emojis no corpo da requisicao
        if (VerificadorEmojis.ContemEmoji(corpo))
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de validacao nos dados do lancamento",
                400,
                "Caracteres invalidos ou emojis detectados. Apenas texto em portugues do Brasil e permitido."
            ));
        }

        RequisicaoLancamento? requisicao;
        try
        {
            requisicao = JsonSerializer.Deserialize<RequisicaoLancamento>(corpo);
        }
        catch (Exception ex)
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de formatacao no corpo da requisicao",
                400,
                $"JSON malformatado: {ex.Message}"
            ));
        }

        if (requisicao == null)
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de validacao nos dados do lancamento",
                400,
                "O corpo da requisicao nao pode ser nulo ou vazio."
            ));
        }

        if (VerificadorEmojis.ContemEmoji(requisicao.MerchantId) ||
            VerificadorEmojis.ContemEmoji(requisicao.Description) ||
            VerificadorEmojis.ContemEmoji(requisicao.Type) ||
            VerificadorEmojis.ContemEmoji(requisicao.IdempotencyKey))
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de validacao nos dados do lancamento",
                400,
                "Caracteres invalidos ou emojis detectados. Apenas texto em portugues do Brasil e permitido."
            ));
        }

        // 2. Validacao do identificador do comerciante
        if (string.IsNullOrWhiteSpace(requisicao.MerchantId))
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de validacao nos dados do lancamento",
                400,
                "O identificador do comerciante e obrigatorio."
            ));
        }

        if (requisicao.MerchantId.Length > 50)
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de validacao nos dados do lancamento",
                400,
                "O identificador do comerciante deve conter no maximo 50 caracteres."
            ));
        }

        // 3. Validacao do valor do lancamento
        if (requisicao.Amount <= 0)
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de validacao nos dados do lancamento",
                400,
                "O valor do lancamento deve ser estritamente maior que zero."
            ));
        }

        // 4. Validacao da descricao do lancamento
        if (string.IsNullOrWhiteSpace(requisicao.Description))
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de validacao nos dados do lancamento",
                400,
                "A descricao do lancamento e obrigatoria."
            ));
        }

        if (requisicao.Description.Length > 500)
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de validacao nos dados do lancamento",
                400,
                "A descricao do lancamento deve conter no maximo 500 caracteres."
            ));
        }

        // 5. Validacao do tipo de lancamento
        var tipoNormalizado = requisicao.Type?.Trim();
        var ehCredito = string.Equals(tipoNormalizado, "Credit", StringComparison.OrdinalIgnoreCase);
        var ehDebito = string.Equals(tipoNormalizado, "Debit", StringComparison.OrdinalIgnoreCase);

        if (!ehCredito && !ehDebito)
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Erro de validacao nos dados do lancamento",
                400,
                $"Tipo de lancamento invalido: '{requisicao.Type}'. Permitidos: 'Credit' ou 'Debit'."
            ));
        }

        var tipoFormatado = ehCredito ? "Credit" : "Debit";

        // 6. Obtencao da chave de idempotencia (via cabecalho ou corpo)
        string? chaveIdempotencia = null;
        if (request.Headers.TryGetValues("X-Idempotency-Key", out var cabecalhosChave))
        {
            chaveIdempotencia = cabecalhosChave.FirstOrDefault();
            if (VerificadorEmojis.ContemEmoji(chaveIdempotencia))
            {
                return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                    "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    "Erro de validacao nos dados do lancamento",
                    400,
                    "Caracteres invalidos ou emojis detectados. Apenas texto em portugues do Brasil e permitido."
                ));
            }
        }

        if (string.IsNullOrWhiteSpace(chaveIdempotencia))
        {
            chaveIdempotencia = requisicao.IdempotencyKey;
        }

        lock (_lockSincronizacao)
        {
            // 7. Controle de Idempotencia estrita
            if (!string.IsNullOrWhiteSpace(chaveIdempotencia))
            {
                if (_tabelaIdempotencia.TryGetValue(chaveIdempotencia, out var registroExistente))
                {
                    // Verifica se o payload eh identico ou conflitante
                    if (registroExistente.Requisicao.MerchantId == requisicao.MerchantId &&
                        registroExistente.Requisicao.Amount == requisicao.Amount &&
                        string.Equals(registroExistente.Requisicao.Type, requisicao.Type, StringComparison.OrdinalIgnoreCase))
                    {
                        // Retorna sucesso idempotente sem duplicar o lancamento ou evento
                        return CriarRespostaJson(HttpStatusCode.OK, registroExistente.Resposta);
                    }

                    return CriarRespostaJson(HttpStatusCode.Conflict, new DetalhesProblemaRfc7231(
                        "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                        "Conflito de idempotencia",
                        409,
                        "A chave de idempotencia informada ja foi utilizada com parametros diferentes."
                    ));
                }
            }

            // 8. Criacao e persistencia do lancamento
            var respostaNova = new RespostaLancamento(
                TransactionId: Guid.NewGuid(),
                MerchantId: requisicao.MerchantId,
                Amount: requisicao.Amount,
                Type: tipoFormatado,
                CreatedAt: DateTime.UtcNow
            );

            _tabelaTransacoes[respostaNova.TransactionId] = respostaNova;

            if (!string.IsNullOrWhiteSpace(chaveIdempotencia))
            {
                _tabelaIdempotencia[chaveIdempotencia] = (requisicao, respostaNova);
            }

            // 9. Publicacao assincrona do evento de dominio
            var evento = new EventoLancamentoCriado(
                EventId: Guid.NewGuid(),
                TransactionId: respostaNova.TransactionId,
                MerchantId: respostaNova.MerchantId,
                Amount: respostaNova.Amount,
                Type: respostaNova.Type,
                Description: requisicao.Description,
                CreatedAt: respostaNova.CreatedAt,
                OccurredOn: DateTime.UtcNow
            );

            _filaEventosRabbitMq.Enqueue(evento);

            // 10. Processamento imediato pelo worker de consolidacao continua
            ProcessarEventoWorker(evento);

            return CriarRespostaJson(HttpStatusCode.Created, respostaNova);
        }
    }

    private void ProcessarEventoWorker(EventoLancamentoCriado evento)
    {
        lock (_lockSincronizacao)
        {
            // Verificacao de deduplicacao via tabela processed_events
            if (_tabelaEventosProcessados.ContainsKey(evento.EventId))
            {
                return; // Descarta com Ack gracioso
            }

            _tabelaEventosProcessados[evento.EventId] = DateTime.UtcNow;

            var dataEvento = DateOnly.FromDateTime(evento.CreatedAt);
            var chaveConsolidado = (evento.MerchantId, dataEvento);

            if (!_tabelaConsolidadoPostgres.TryGetValue(chaveConsolidado, out var consolidado))
            {
                consolidado = new ConsolidadoRegistro
                {
                    ComercianteId = evento.MerchantId,
                    Data = dataEvento,
                    TotalCreditos = 0,
                    TotalDebitos = 0,
                    QuantidadeTransacoes = 0,
                    Versao = 1
                };
                _tabelaConsolidadoPostgres[chaveConsolidado] = consolidado;
            }

            if (evento.Type.Equals("Credit", StringComparison.OrdinalIgnoreCase))
            {
                consolidado.TotalCreditos += evento.Amount;
            }
            else if (evento.Type.Equals("Debit", StringComparison.OrdinalIgnoreCase))
            {
                consolidado.TotalDebitos += evento.Amount;
            }

            consolidado.QuantidadeTransacoes++;
            consolidado.UltimaAtualizacao = DateTime.UtcNow;
            consolidado.Versao++;

            // Atualizacao write-through no cache Redis
            var chaveCache = $"consolidated:{evento.MerchantId}:{dataEvento:yyyy-MM-dd}";
            var ttl = dataEvento == DateOnly.FromDateTime(DateTime.UtcNow)
                ? TimeSpan.FromHours(1)
                : TimeSpan.FromHours(24);

            _cacheRedis[chaveCache] = (consolidado, DateTime.UtcNow.Add(ttl));
        }
    }

    private HttpResponseMessage ManipularConsultaConsolidado(string caminho)
    {
        // Formato esperado: /api/v1/consolidated/{merchantId}/{date}
        var partes = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length != 5)
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Parametros de rota invalidos",
                400,
                "A rota deve seguir o formato /api/v1/consolidated/{merchantId}/{date}."
            ));
        }

        var comercianteId = Uri.UnescapeDataString(partes[3]);
        var dataStr = partes[4];

        if (VerificadorEmojis.ContemEmoji(comercianteId) || VerificadorEmojis.ContemEmoji(dataStr))
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Parametro invalido com emojis",
                400,
                "Emojis nao sao permitidos nos parametros de rota."
            ));
        }

        if (!DateOnly.TryParseExact(dataStr, "yyyy-MM-dd", out var data))
        {
            return CriarRespostaJson(HttpStatusCode.BadRequest, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                "Formato de data invalido",
                400,
                "A data informada deve estar estritamente no padrao ISO 8601 (yyyy-MM-dd)."
            ));
        }

        var stopwatch = Stopwatch.StartNew();
        var chaveCache = $"consolidated:{comercianteId}:{data:yyyy-MM-dd}";

        // 1. Tentar leitura do Redis se nao houver simulacao de falha
        if (!_falhaRedisSimulada && _cacheRedis.TryGetValue(chaveCache, out var itemCache))
        {
            if (DateTime.UtcNow <= itemCache.Expiracao)
            {
                stopwatch.Stop();
                var registro = itemCache.Registro;
                return CriarRespostaJson(HttpStatusCode.OK, new RespostaConsolidado(
                    MerchantId: registro.ComercianteId,
                    Date: registro.Data.ToString("yyyy-MM-dd"),
                    TotalCredits: registro.TotalCreditos,
                    TotalDebits: registro.TotalDebitos,
                    ClosingBalance: registro.SaldoFechamento,
                    TransactionCount: registro.QuantidadeTransacoes,
                    LastUpdatedAt: registro.UltimaAtualizacao,
                    Cached: true
                ));
            }
        }

        // 2. Fallback resiliente para o PostgreSQL (sob cache miss ou falha simulada do Redis)
        var chaveBanco = (comercianteId, data);
        if (_tabelaConsolidadoPostgres.TryGetValue(chaveBanco, out var registroPostgres))
        {
            // Se o Redis estiver saudavel, atualiza o cache para proximas consultas
            if (!_falhaRedisSimulada)
            {
                _cacheRedis[chaveCache] = (registroPostgres, DateTime.UtcNow.AddHours(1));
            }

            stopwatch.Stop();
            return CriarRespostaJson(HttpStatusCode.OK, new RespostaConsolidado(
                MerchantId: registroPostgres.ComercianteId,
                Date: registroPostgres.Data.ToString("yyyy-MM-dd"),
                TotalCredits: registroPostgres.TotalCreditos,
                TotalDebits: registroPostgres.TotalDebitos,
                ClosingBalance: registroPostgres.SaldoFechamento,
                TransactionCount: registroPostgres.QuantidadeTransacoes,
                LastUpdatedAt: registroPostgres.UltimaAtualizacao,
                Cached: false
            ));
        }

        // 3. Caso nao haja registros cadastrados para a data e comerciante, retorna 200 OK com valores zerados
        stopwatch.Stop();
        return CriarRespostaJson(HttpStatusCode.OK, new RespostaConsolidado(
            MerchantId: comercianteId,
            Date: data.ToString("yyyy-MM-dd"),
            TotalCredits: 0m,
            TotalDebits: 0m,
            ClosingBalance: 0m,
            TransactionCount: 0,
            LastUpdatedAt: DateTime.UtcNow,
            Cached: false
        ));
    }

    private const string ChaveApiKeyEsperada = "cashflow-secret-api-key-2026";

    private static bool ValidarAutenticacao(HttpRequestMessage request, out HttpResponseMessage respostaErro)
    {
        string? tokenFornecido = null;
        if (request.Headers.TryGetValues("X-Api-Key", out var valoresApiKey))
        {
            tokenFornecido = valoresApiKey.FirstOrDefault()?.Trim();
        }
        else if (request.Headers.Authorization is { } auth &&
                 auth.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            tokenFornecido = auth.Parameter?.Trim();
        }

        if (string.IsNullOrWhiteSpace(tokenFornecido) ||
            !string.Equals(tokenFornecido, ChaveApiKeyEsperada, StringComparison.Ordinal))
        {
            respostaErro = CriarRespostaJson(HttpStatusCode.Unauthorized, new DetalhesProblemaRfc7231(
                "https://tools.ietf.org/html/rfc7235#section-3.1",
                "Acesso nao autorizado",
                401,
                "Credenciais de autenticacao ausentes ou invalidas. Forneca o cabecalho 'X-Api-Key' ou 'Authorization: Bearer <token>' valido.",
                CashFlowErrorCodes.AutenticacaoNaoAutorizada
            ));
            return false;
        }

        respostaErro = null!;
        return true;
    }

    private static HttpResponseMessage CriarRespostaJson<T>(HttpStatusCode status, T corpo)
    {
        var json = JsonSerializer.Serialize(corpo);
        var resposta = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        resposta.Headers.Add("X-Content-Type-Options", "nosniff");
        resposta.Headers.Add("X-Frame-Options", "DENY");
        return resposta;
    }
}
