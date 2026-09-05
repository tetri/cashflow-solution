using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier1_CoberturaFuncionalidades;

/// <summary>
/// Suite de testes E2E do Tier 1 para a funcionalidade de Registro de Debito.
/// Valida operacoes financeiras de saida de recursos, deducao correta de saldo
/// e conformidade com as regras arquiteturais da solucao de Fluxo de Caixa.
/// </summary>
public class TestesRegistroDebitoTier1 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesRegistroDebitoTier1()
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
    /// Cenario: Registro de debito valido com verificacao de deducao no saldo consolidado.
    /// Justificativa: Assegura que um lancamento do tipo 'Debit' reduza o saldo de fechamento
    /// na exata proporcao do valor informado.
    /// </summary>
    [Fact]
    public async Task DeveRegistrarDebitoComSucessoEDeduzirDoSaldoConsolidado()
    {
        // Arranjo: Cria primeiro um credito inicial de R$ 500,00 e em seguida um debito de R$ 150,00
        var comercianteId = "COMERCIANTE_DEB_001";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 500.00m, "Credit", "Carga inicial"));

        var requisicaoDebito = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 150.00m,
            Type: "Debit",
            Description: "Pagamento de fatura de energia eletrica"
        );

        // Acao
        var respostaDebito = await _cliente.RegistrarLancamentoAsync(requisicaoDebito);

        // Assercoes
        respostaDebito.Should().NotBeNull();
        respostaDebito!.Type.Should().Be("Debit");
        respostaDebito.Amount.Should().Be(150.00m);

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.TotalCredits.Should().Be(500.00m);
        consolidado.TotalDebits.Should().Be(150.00m);
        consolidado.ClosingBalance.Should().Be(350.00m, "500 de credito menos 150 de debito resulta em saldo 350");
        consolidado.TransactionCount.Should().Be(2);
    }

    /// <summary>
    /// Cenario: Resposta HTTP 201 com contrato integro para lancamentos de debito.
    /// Justificativa: Garante que os dados retornados no payload reflitam fielmente
    /// o identificador gerado e os parametros fornecidos na requisicao.
    /// </summary>
    [Fact]
    public async Task DeveRetornarCodigo201CreatedComDadosCorretosDoDebito()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_DEB_002",
            Amount: 89.90m,
            Type: "Debit",
            Description: "Compra de material de escritorio"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Created);
        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);
        lancamento.Should().NotBeNull();
        lancamento!.Type.Should().Be("Debit");
        lancamento.Amount.Should().Be(89.90m);
        lancamento.TransactionId.Should().NotBeEmpty();
    }

    /// <summary>
    /// Cenario: Publicacao de evento assincrono TransactionCreatedEvent para debito.
    /// Justificativa: Comprova que eventos de debito sao encaminhados ao barramento de mensageria
    /// com a indicacao correta do tipo 'Debit'.
    /// </summary>
    [Fact]
    public async Task DevePublicarEventoDeDominioParaLancamentoDeDebito()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_DEB_003";
        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 430.00m,
            Type: "Debit",
            Description: "Pagamento de fornecedor de alimentos"
        );

        // Acao
        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);

        // Assercoes
        var eventos = _ambiente.ObterEventosPublicados();
        eventos.Should().Contain(e =>
            e.TransactionId == lancamento!.TransactionId &&
            e.MerchantId == comercianteId &&
            e.Amount == 430.00m &&
            e.Type == "Debit"
        );
    }

    /// <summary>
    /// Cenario: Permissao de debito quando nao ha creditos previos (saldo negativo).
    /// Justificativa: Em controle de fluxo de caixa gerencial, debitos sem fundos pre-existentes
    /// sao permitidos e devem gerar saldo devedor negativo para alertar a administracao financeira.
    /// </summary>
    [Fact]
    public async Task DevePermitirDebitoComSaldoInicialZeroGerandoSaldoNegativo()
    {
        // Arranjo: Comerciante sem lancamentos pre-existentes
        var comercianteId = "COMERCIANTE_DEB_004";
        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 75.50m,
            Type: "Debit",
            Description: "Tarifa bancaria de manutencao de conta"
        );

        // Acao
        await _cliente.RegistrarLancamentoAsync(requisicao);

        // Assercoes no saldo consolidado
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.TotalCredits.Should().Be(0m);
        consolidado.TotalDebits.Should().Be(75.50m);
        consolidado.ClosingBalance.Should().Be(-75.50m, "debito sem creditos deve resultar em saldo negativo");
        consolidado.TransactionCount.Should().Be(1);
    }

    /// <summary>
    /// Cenario: Multiplos debitos sucessivos acumulando no total diário de saidas.
    /// Justificativa: Valida a monotonicidade do total de debitos ao longo de operacoes repetidas.
    /// </summary>
    [Fact]
    public async Task DeveProcessarMultiplosDebitosIncrementandoTotalDebitosCorretamente()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_DEB_005";
        var debitos = new[] { 10.00m, 20.50m, 35.25m, 14.25m };

        // Acao
        foreach (var valor in debitos)
        {
            await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
                MerchantId: comercianteId,
                Amount: valor,
                Type: "Debit",
                Description: $"Pagamento parcial no valor de {valor}"
            ));
        }

        // Assercoes
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.TotalDebits.Should().Be(80.00m, "a soma de 10.00 + 20.50 + 35.25 + 14.25 deve ser 80.00");
        consolidado.ClosingBalance.Should().Be(-80.00m);
        consolidado.TransactionCount.Should().Be(4);
    }
}
