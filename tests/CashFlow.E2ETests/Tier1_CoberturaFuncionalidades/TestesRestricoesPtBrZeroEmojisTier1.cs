using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier1_CoberturaFuncionalidades;

/// <summary>
/// Suite de testes E2E do Tier 1 para validacao de restricoes de idioma (pt-BR culto)
/// e cumprimento estrito da diretriz inegociavel de ZERO EMOJIS em todo o ecossistema.
/// </summary>
public class TestesRestricoesPtBrZeroEmojisTier1 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesRestricoesPtBrZeroEmojisTier1()
    {
        _ambiente = new AmbienteTesteE2E();
        _cliente = new ClienteApiFluxoCaixa(_ambiente.CriarClienteHttp());
    }

    public void Dispose()
    {
        _ambiente.LimparEstado();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Cenario: Requisicao com caractere emoji na descricao ou identificador.
    /// Justificativa: A politica institucional de higienizacao proibe emojis em qualquer
    /// transacao ou persistencia; a API deve rejeitar sumariamente com HTTP 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarRequisicaoDeLancamentoContendoEmojisComErro400()
    {
        // Arranjo: Cria string com emoji gerado dinamicamente via code point Unicode para nao conter caractere literal
        var emojiCodePoint = char.ConvertFromUtf32(0x1F600); // Emoticon sorridente
        var descricaoComEmoji = $"Venda realizada no balcao {emojiCodePoint}";

        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_EMOJI_001",
            Amount: 100m,
            Type: "Credit",
            Description: descricaoComEmoji
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes.Should().NotBeNull();
        detalhes!.Detail.Should().Contain("emojis detectados", "a mensagem deve explicitar a proibicao de emojis");
    }

    /// <summary>
    /// Cenario: Validacao de mensagens de erro emitidas no padrao RFC 7231 ProblemDetails em pt-BR culto.
    /// Justificativa: Todos os titulos e detalhes de validacao devem ser redigidos em portugues formal,
    /// sem jargoes informais ou termos em ingles nao traduzidos.
    /// </summary>
    [Fact]
    public async Task DeveRetornarMensagensDeValidacaoRFC7231EmPortuguesCulto()
    {
        // Arranjo: Requisicao com valor negativo invalido
        var requisicaoInvalida = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_PTBR_002",
            Amount: -25m,
            Type: "Credit",
            Description: "Tentativa de credito com valor negativo"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicaoInvalida);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes.Should().NotBeNull();
        detalhes!.Title.Should().Be("Erro de validacao nos dados do lancamento");
        detalhes.Detail.Should().Be("O valor do lancamento deve ser estritamente maior que zero.");
    }

    /// <summary>
    /// Cenario: Ausencia total de emojis em qualquer payload de resposta HTTP da API.
    /// Justificativa: O VerificadorEmojis inspeciona cada byte das respostas de sucesso e erro
    /// assegurando conformidade de 100% com a regra inegociavel do projeto.
    /// </summary>
    [Fact]
    public async Task DeveGarantirQueTodasAsRespostasHttpNaoContenhamNenhumEmoji()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_PTBR_003";
        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 450.00m,
            Type: "Credit",
            Description: "Liquidacao de duplicata bancaria"
        );

        // Acao: Registra lancamento e consulta consolidado
        var respostaLancamento = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);
        var corpoLancamento = await respostaLancamento.Content.ReadAsStringAsync();

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var respostaConsolidado = await _cliente.ConsultarConsolidadoBrutoAsync(comercianteId, dataHoje);
        var corpoConsolidado = await respostaConsolidado.Content.ReadAsStringAsync();

        // Assercoes de zero emojis em todos os payloads
        VerificadorEmojis.ContemEmoji(corpoLancamento).Should().BeFalse("o JSON de resposta do lancamento nao pode conter emojis");
        VerificadorEmojis.ContemEmoji(corpoConsolidado).Should().BeFalse("o JSON de resposta do consolidado nao pode conter emojis");
    }

    /// <summary>
    /// Cenario: Aceitacao e preservacao integra de caracteres do portugues (acentos e cedilha).
    /// Justificativa: Textos legitimos em pt-BR contendo acentos circunflexos, agudos, tis e cedilhas
    /// devem ser processados sem falhas de encoding (mojibake).
    /// </summary>
    [Fact]
    public async Task DeveAceitarTextosEmPortuguesComAcentosECedilhaSemErrosDeCodificacao()
    {
        // Arranjo
        var textoComplexo = "Operação de aquisição: café, açúcar, pão e maças para refeição coletiva";
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_PTBR_004",
            Amount: 185.30m,
            Type: "Debit",
            Description: textoComplexo
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Created);
        var eventos = _ambiente.ObterEventosPublicados();
        var evento = eventos.FirstOrDefault(e => e.MerchantId == "COMERCIANTE_PTBR_004");
        evento.Should().NotBeNull();
        evento!.Description.Should().Be(textoComplexo);
    }

    /// <summary>
    /// Cenario: Bloqueio de emojis enviados em parametros de rota na URL.
    /// Justificativa: Previne ataques de injecao ou corrupcao de chaves de roteamento / Redis
    /// causadas por caracteres especiais nao-alfanumericos nos caminhos da API.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarTentativaDeConsultaDeConsolidadoComEmojisNaRota()
    {
        // Arranjo: Rota contendo emoji codificado na URL
        var emojiCodePoint = char.ConvertFromUtf32(0x1F600);
        var comercianteComEmoji = $"COMERCIANTE_{emojiCodePoint}";
        var dataHoje = "2026-09-04";

        // Acao
        var respostaHttp = await _cliente.ConsultarConsolidadoBrutoAsync(comercianteComEmoji, dataHoje);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes!.Detail.Should().Contain("Emojis nao sao permitidos");
    }
}
