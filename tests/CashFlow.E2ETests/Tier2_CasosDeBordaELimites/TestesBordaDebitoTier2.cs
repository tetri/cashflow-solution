using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier2_CasosDeBordaELimites;

/// <summary>
/// Suite de testes E2E do Tier 2 para Casos de Borda e Limites em Registro de Debito.
/// Valida integridade em valores nulos, limites de texto em descricoes
/// e estrita conformidade com as regras de negocio financeiras.
/// </summary>
public class TestesBordaDebitoTier2 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesBordaDebitoTier2()
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
    /// Cenario: Registro de debito com valor zero (R$ 0,00).
    /// Justificativa: Nao sao permitidas saidas financeiras de valor zero.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarDebitoComValorZeroRetornandoErro400()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_DEB_001",
            Amount: 0.00m,
            Type: "Debit",
            Description: "Debito zerado"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes!.Detail.Should().Be("O valor do lancamento deve ser estritamente maior que zero.");
    }

    /// <summary>
    /// Cenario: Registro de debito com valor negativo.
    /// Justificativa: Evita tentativa de registrar debito negativo que causaria efeito de credito.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarDebitoComValorNegativoRetornandoErro400()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_DEB_002",
            Amount: -50.00m,
            Type: "Debit",
            Description: "Tentativa de debito negativo"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes!.Status.Should().Be(400);
    }

    /// <summary>
    /// Cenario: Requisicao com descricao vazia ou composta apenas por espacos em branco.
    /// Justificativa: Auditorias contabeis exigem descricao inteligivel para cada lancamento.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarDebitoComDescricaoVaziaRetornandoErro400()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_DEB_003",
            Amount: 100.00m,
            Type: "Debit",
            Description: "    " // Espacos em branco
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes!.Detail.Should().Be("A descricao do lancamento e obrigatoria.");
    }

    /// <summary>
    /// Cenario: Descricao no limite maximo exato de 500 caracteres.
    /// Justificativa: Testa o tamanho maximo contratual permitido para notas fiscais e justificativas.
    /// </summary>
    [Fact]
    public async Task DeveAceitarDebitoComDescricaoNoLimiteExatoDeQuinhentosCaracteres()
    {
        // Arranjo: String com exatamente 500 caracteres
        var descricao500Chars = new string('A', 500);
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_DEB_004",
            Amount: 230.40m,
            Type: "Debit",
            Description: descricao500Chars
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Cenario: Descricao com 501 caracteres (excedendo limite em 1 caractere).
    /// Justificativa: BVA para impedir truncamento silencioso no banco de dados.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarDebitoComDescricaoExcedendoQuinhentosCaracteres()
    {
        // Arranjo: String com 501 caracteres
        var descricao501Chars = new string('B', 501);
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_DEB_005",
            Amount: 120.00m,
            Type: "Debit",
            Description: descricao501Chars
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes!.Detail.Should().Contain("maximo 500 caracteres");
    }
}
