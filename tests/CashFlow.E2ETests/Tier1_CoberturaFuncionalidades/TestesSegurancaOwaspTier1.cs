using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using CashFlow.Shared.Domain.Errors;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier1_CoberturaFuncionalidades;

/// <summary>
/// Suite de testes E2E do Tier 1 para conformidade rigorosa com OWASP API Security Top 10:
/// 1. Autenticacao e autorizacao obrigatoria (OWASP API1 e API2) via cabecalho X-Api-Key ou Bearer Token.
/// 2. Respostas de erro padronizadas RFC 7807 com codigos unicos legiveis por maquina (CWE-209).
/// 3. Cabecalhos de seguranca HTTP obrigatorios (nosniff, DENY).
/// </summary>
public class TestesSegurancaOwaspTier1 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _clienteAutenticado;
    private readonly ClienteApiFluxoCaixa _clienteComChaveInvalida;

    public TestesSegurancaOwaspTier1()
    {
        _ambiente = new AmbienteTesteE2E();
        var httpClient = _ambiente.CriarClienteHttp();
        _clienteAutenticado = new ClienteApiFluxoCaixa(httpClient, ClienteApiFluxoCaixa.ChaveApiKeyPadrao);
        _clienteComChaveInvalida = new ClienteApiFluxoCaixa(httpClient, "chave-maliciosa-ou-invalida");
    }

    public void Dispose()
    {
        _ambiente.LimparEstado();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DeveRejeitarRegistroDeLancamentoSemAutenticacaoComStatus401EErrorCode()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "LOJA_OWASP_001",
            Amount: 150m,
            Type: "Credit",
            Description: "Tentativa de lancamento anonimo"
        );

        // Acao: Chamada sem credenciais de autenticacao
        var respostaHttp = await _clienteAutenticado.RegistrarLancamentoSemAutenticacaoAsync(requisicao);

        // Assercoes OWASP
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problema = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        problema.Should().NotBeNull();
        problema!.Status.Should().Be(401);
        problema.ErrorCode.Should().Be(CashFlowErrorCodes.AutenticacaoNaoAutorizada);
    }

    [Fact]
    public async Task DeveRejeitarConsultaDeConsolidadoSemAutenticacaoComStatus401EErrorCode()
    {
        // Acao: Chamada sem credenciais de autenticacao
        var respostaHttp = await _clienteAutenticado.ConsultarConsolidadoSemAutenticacaoAsync("LOJA_OWASP_001", "2026-09-05");

        // Assercoes OWASP
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problema = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        problema.Should().NotBeNull();
        problema!.Status.Should().Be(401);
        problema.ErrorCode.Should().Be(CashFlowErrorCodes.AutenticacaoNaoAutorizada);
    }

    [Fact]
    public async Task DeveRejeitarRequisicaoComChaveDeApiInvalidaComStatus401()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "LOJA_OWASP_001",
            Amount: 200m,
            Type: "Credit",
            Description: "Tentativa com chave incorreta"
        );

        // Acao
        var respostaHttp = await _clienteComChaveInvalida.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problema = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        problema.Should().NotBeNull();
        problema!.ErrorCode.Should().Be(CashFlowErrorCodes.AutenticacaoNaoAutorizada);
    }

    [Fact]
    public async Task DeveAceitarRequisicaoComChaveDeApiValidaERetornarCabecalhosDeSegurancaOwasp()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "LOJA_OWASP_001",
            Amount: 320m,
            Type: "Credit",
            Description: "Lancamento autenticado conforme padrao OWASP"
        );

        // Acao
        var respostaHttp = await _clienteAutenticado.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Created);
        respostaHttp.Headers.Should().ContainKey("X-Content-Type-Options");
        respostaHttp.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        respostaHttp.Headers.Should().ContainKey("X-Frame-Options");
        respostaHttp.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
    }
}
