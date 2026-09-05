using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier2_CasosDeBordaELimites;

/// <summary>
/// Suite de testes E2E do Tier 2 para Casos de Borda e Limites em Restricoes pt-BR e Zero Emojis.
/// Inspeciona cabecalhos, campos individuais de payload, ausencia de falsos-positivos com
/// acentuacao do portugues e garantia de respostas de erro puras sem qualquer caractere grafico.
/// </summary>
public class TestesBordaPtBrZeroEmojisTier2 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesBordaPtBrZeroEmojisTier2()
    {
        _ambiente = new AmbienteTesteE2E();
        _cliente = new ClienteApiFluxoCaixa(_ambiente.CriarClienteHttp());
    }

    public void Dispose()
    {
        _ambiente.LimparEstado();
    }

    /// <summary>
    /// Cenario: Envio de caractere emoji embutido no cabecalho HTTP X-Idempotency-Key.
    /// Justificativa: Garante que os cabecalhos HTTP tambem passem pelo filtro de higienizacao
    /// institucional de zero emojis.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarEmojiNoCabecalhoHttpDeIdempotencia()
    {
        // Arranjo: Cria emoji via Unicode escape
        var emoji = char.ConvertFromUtf32(0x1F680); // Foguete
        var cabecalhoComEmoji = $"CHAVE-{emoji}-123";

        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_PTBR_001",
            Amount: 100m,
            Type: "Credit",
            Description: "Lancamento com cabecalho irregular"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao, cabecalhoComEmoji);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes!.Detail.Should().Contain("emojis detectados");
    }

    /// <summary>
    /// Cenario: Tentativa de envio de emoji no campo MerchantId.
    /// Justificativa: O identificador de entidade nao pode conter caracteres nao-alfanumericos graficos.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarEmojiNoCampoMerchantIdComErro400()
    {
        // Arranjo
        var emoji = char.ConvertFromUtf32(0x1F4B0); // Saco de dinheiro
        var requisicao = new RequisicaoLancamento(
            MerchantId: $"LOJA_{emoji}_01",
            Amount: 200m,
            Type: "Credit",
            Description: "Tentativa de cadastro de comerciante com emoji"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes!.Detail.Should().Contain("emojis detectados");
    }

    /// <summary>
    /// Cenario: Tentativa de envio de emoji no campo Type (tipo de transacao).
    /// Justificativa: Impede que o campo de discriminacao de tipo receba simbolos visuais.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarEmojiNoCampoTypeComErro400()
    {
        // Arranjo
        var emoji = char.ConvertFromUtf32(0x1F44D); // Polegar para cima
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_PTBR_003",
            Amount: 150m,
            Type: $"Credit_{emoji}",
            Description: "Tipo corrompido"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Cenario: Inspecao rigorosa de ausencia de emojis nas proprias mensagens de erro emitidas pela API.
    /// Justificativa: Respostas de erro, rastreamentos e detalhes no ProblemDetails nao devem conter
    /// nenhum caractere emoji ilustrativo ou decorativo.
    /// </summary>
    [Fact]
    public async Task DeveGarantirAusenciaDeEmojisNosDetalhesDeMensagensDeErro400()
    {
        // Arranjo: Dispara requisicao com erro proposital
        var requisicaoInvalida = new RequisicaoLancamento(
            MerchantId: "", // Invalido
            Amount: -10m,   // Invalido
            Type: "Desconhecido",
            Description: ""
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicaoInvalida);
        var conteudoErro = await respostaHttp.Content.ReadAsStringAsync();

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VerificadorEmojis.ContemEmoji(conteudoErro).Should().BeFalse("a resposta de erro da API nao deve conter nenhum emoji");
    }

    /// <summary>
    /// Cenario: Aceitacao de textos complexos em portugues (acentos, pontuacao, hifens, aspas) sem falso positivo.
    /// Justificativa: O VerificadorEmojis deve ter alta especificidade, aceitando pontuacao formal
    /// e todo o conjunto lexico da lingua portuguesa culta sem rejeitar indevidamente.
    /// </summary>
    [Fact]
    public async Task DeveAceitarTextosExtensosEmPortuguesComPontuacaoECaracteresAcentuadosSemFalsoPositivo()
    {
        // Arranjo: Paragrafo formal de negocio em pt-BR culto
        var descricaoComplexa = "Aquisição de itens para manutenção: parafusos de aço, graxa sintética " +
                                "e óleo lubrificante para motor à combustão — Fatura nº 98.765/2026 " +
                                "(Aprovação da Diretoria de Operações).";

        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_PTBR_005",
            Amount: 1850.75m,
            Type: "Debit",
            Description: descricaoComplexa
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Created, "textos legitimos em lingua portuguesa nao devem sofrer falso-positivo");
        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);
        lancamento.Should().NotBeNull();
    }
}
