using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier2_CasosDeBordaELimites;

/// <summary>
/// Suite de testes E2E do Tier 2 para Casos de Borda e Limites em Registro de Credito.
/// Valida condicoes extremas de valores numericos, limites de tamanho de campos
/// e robustez do tratamento de entradas invalidas.
/// </summary>
public class TestesBordaCreditoTier2 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesBordaCreditoTier2()
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
    /// Cenario: Tentativa de registro de credito com valor zero (R$ 0,00).
    /// Justificativa: Transacoes sem valor financeiro sao estritamente proibidas
    /// pela regra de negocio, devendo falhar com HTTP 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarCreditoComValorZeroRetornandoErro400()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_CRED_001",
            Amount: 0.00m,
            Type: "Credit",
            Description: "Credito zerado de teste"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes.Should().NotBeNull();
        detalhes!.Detail.Should().Be("O valor do lancamento deve ser estritamente maior que zero.");
    }

    /// <summary>
    /// Cenario: Tentativa de registro de credito com valor negativo.
    /// Justificativa: Creditos com valor negativo poderiam subverter a contabilidade
    /// agindo como debitos disfarcados; a validacao deve barrar na borda da API.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarCreditoComValorNegativoRetornandoErro400()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_CRED_002",
            Amount: -100.50m,
            Type: "Credit",
            Description: "Tentativa de credito negativo"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes.Should().NotBeNull();
        detalhes!.Status.Should().Be(400);
    }

    /// <summary>
    /// Cenario: Registro de credito com valor financeiro extremo (limite superior decimal).
    /// Justificativa: Valida a capacidade de processamento monetario de grandes corporacoes
    /// sem estouro de capacidade (overflow) ou perda de centavos.
    /// </summary>
    [Fact]
    public async Task DevePermitirCreditoComValorDecimalExtremoPreservandoPrecisao()
    {
        // Arranjo: R$ 999.999.999.999,99 (quase 1 trilhao de reais)
        var valorExtremo = 999999999999.99m;
        var comercianteId = "COMERCIANTE_BORDA_CRED_003";
        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: valorExtremo,
            Type: "Credit",
            Description: "Liquidacao interbancaria corporativa de alto valor"
        );

        // Acao
        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);

        // Assercoes
        lancamento.Should().NotBeNull();
        lancamento!.Amount.Should().Be(valorExtremo);

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado!.TotalCredits.Should().Be(valorExtremo);
        consolidado.ClosingBalance.Should().Be(valorExtremo);
    }

    /// <summary>
    /// Cenario: Identificador de comerciante com exatos 50 caracteres (limite permitido).
    /// Justificativa: Testa o limite maximo de contencao da coluna merchant_id.
    /// </summary>
    [Fact]
    public async Task DeveAceitarComercianteComTamanhoExatoDeCinquentaCaracteres()
    {
        // Arranjo: String com exatamente 50 caracteres alfanumericos
        var comerciante50Chars = new string('M', 50);
        var requisicao = new RequisicaoLancamento(
            MerchantId: comerciante50Chars,
            Amount: 50.00m,
            Type: "Credit",
            Description: "Teste de limite exato de 50 caracteres no MerchantId"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Created);
        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);
        lancamento!.MerchantId.Should().Be(comerciante50Chars);
    }

    /// <summary>
    /// Cenario: Identificador de comerciante com 51 caracteres (excedendo limite em 1 caractere).
    /// Justificativa: BVA (Boundary Value Analysis) para garantir rejeicao imediata no ponto n+1.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarComercianteComMaisDeCinquentaCaracteresRetornandoErro400()
    {
        // Arranjo: String com 51 caracteres
        var comerciante51Chars = new string('X', 51);
        var requisicao = new RequisicaoLancamento(
            MerchantId: comerciante51Chars,
            Amount: 75.00m,
            Type: "Credit",
            Description: "Teste de estouro de limite no MerchantId"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes!.Detail.Should().Contain("maximo 50 caracteres");
    }
}
