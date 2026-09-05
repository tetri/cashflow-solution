using System.Text;
using System.Text.Json;

namespace CashFlow.E2ETests.Infraestrutura;

/// <summary>
/// Cliente HTTP tipado para execucao de operacoes da suite E2E contra a API do Fluxo de Caixa.
/// Permite tanto o consumo com desserializacao automatica quanto a inspecao de respostas brutas.
/// </summary>
public class ClienteApiFluxoCaixa
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ClienteApiFluxoCaixa(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Envia uma requisicao POST para /api/v1/transactions e retorna a resposta HTTP bruta.
    /// </summary>
    public async Task<HttpResponseMessage> RegistrarLancamentoBrutoAsync(
        RequisicaoLancamento requisicao,
        string? chaveIdempotenciaCabecalho = null)
    {
        var json = JsonSerializer.Serialize(requisicao);
        return await RegistrarLancamentoJsonBrutoAsync(json, chaveIdempotenciaCabecalho);
    }

    /// <summary>
    /// Envia uma requisicao POST com conteudo JSON arbitrario (util para testes adversariais e borda).
    /// </summary>
    public async Task<HttpResponseMessage> RegistrarLancamentoJsonBrutoAsync(
        string conteudoJson,
        string? chaveIdempotenciaCabecalho = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transactions")
        {
            Content = new StringContent(conteudoJson, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(chaveIdempotenciaCabecalho))
        {
            request.Headers.Add("X-Idempotency-Key", chaveIdempotenciaCabecalho);
        }

        return await _httpClient.SendAsync(request);
    }

    /// <summary>
    /// Registra um lancamento financeiro e desserializa a resposta para o modelo tipado.
    /// </summary>
    public async Task<RespostaLancamento?> RegistrarLancamentoAsync(
        RequisicaoLancamento requisicao,
        string? chaveIdempotenciaCabecalho = null)
    {
        var respostaHttp = await RegistrarLancamentoBrutoAsync(requisicao, chaveIdempotenciaCabecalho);
        respostaHttp.EnsureSuccessStatusCode();

        var conteudo = await respostaHttp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<RespostaLancamento>(conteudo, JsonOptions);
    }

    /// <summary>
    /// Envia uma requisicao GET para /api/v1/consolidated/{merchantId}/{date} e retorna a resposta HTTP bruta.
    /// </summary>
    public async Task<HttpResponseMessage> ConsultarConsolidadoBrutoAsync(string comercianteId, string dataIso)
    {
        var uri = $"/api/v1/consolidated/{Uri.EscapeDataString(comercianteId)}/{dataIso}";
        return await _httpClient.GetAsync(uri);
    }

    /// <summary>
    /// Consulta o saldo consolidado diario e desserializa a resposta para o modelo tipado.
    /// </summary>
    public async Task<RespostaConsolidado?> ConsultarConsolidadoAsync(string comercianteId, string dataIso)
    {
        var respostaHttp = await ConsultarConsolidadoBrutoAsync(comercianteId, dataIso);
        respostaHttp.EnsureSuccessStatusCode();

        var conteudo = await respostaHttp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<RespostaConsolidado>(conteudo, JsonOptions);
    }

    /// <summary>
    /// Desserializa o corpo de uma resposta com status de erro no padrao RFC 7231 ProblemDetails.
    /// </summary>
    public static async Task<DetalhesProblemaRfc7231?> ExtrairDetalhesProblemaAsync(HttpResponseMessage resposta)
    {
        var conteudo = await resposta.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<DetalhesProblemaRfc7231>(conteudo, JsonOptions);
    }
}
