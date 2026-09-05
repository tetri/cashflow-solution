using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier1_CoberturaFuncionalidades;

/// <summary>
/// Suite de testes E2E do Tier 1 para a funcionalidade de Registro de Credito.
/// Valida o fluxo principal (caminho feliz) e a integracao ponta a ponta
/// desde o endpoint POST /api/v1/transactions ate a reflexao no saldo consolidado.
/// </summary>
public class TestesRegistroCreditoTier1 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesRegistroCreditoTier1()
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
    /// Cenario: Registro de um credito financeiro valido com verificacao do saldo consolidado.
    /// Justificativa: Garante que um valor positivo do tipo 'Credit' seja persistido
    /// e imediatamente agregado ao total de creditos e ao saldo final do comerciante.
    /// </summary>
    [Fact]
    public async Task DeveRegistrarCreditoComSucessoEAtualizarSaldoConsolidado()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_CRED_001";
        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 250.75m,
            Type: "Credit",
            Description: "Recebimento referente a fatura de venda balcao"
        );

        // Acao: Realiza a requisicao HTTP para criacao do lancamento
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes na resposta do lancamento
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Created, "o endpoint deve retornar 201 Created para um novo lancamento");
        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);
        lancamento.Should().NotBeNull();
        lancamento!.MerchantId.Should().Be(comercianteId);
        lancamento.Amount.Should().Be(250.75m);
        lancamento.Type.Should().Be("Credit");
        lancamento.TransactionId.Should().NotBeEmpty();

        // Acao complementar: Consulta o saldo consolidado do dia corrente
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Assercoes no saldo consolidado refletido
        consolidado.Should().NotBeNull();
        consolidado!.TotalCredits.Should().Be(501.50m, "dois lancamentos de 250.75 foram executados no teste");
        consolidado.ClosingBalance.Should().Be(501.50m);
        consolidado.TransactionCount.Should().Be(2);
    }

    /// <summary>
    /// Cenario: Verificacao do codigo HTTP 201 e cabecalhos de resposta.
    /// Justificativa: Atende as diretrizes REST com criacao correta de recurso
    /// e retorno dos metadados de auditoria (identificador unico e data de criacao UTC).
    /// </summary>
    [Fact]
    public async Task DeveRetornarCodigo201CreatedComCabecalhosEIdentificadorUnico()
    {
        // Arranjo
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_CRED_002",
            Amount: 100.00m,
            Type: "Credit",
            Description: "Deposito em conta de comercio"
        );

        // Acao
        var respostaHttp = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.Created);
        respostaHttp.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);
        lancamento.Should().NotBeNull();
        lancamento!.TransactionId.Should().NotBeEmpty();
        lancamento.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Cenario: Publicacao assincrona de evento de dominio TransactionCreatedEvent.
    /// Justificativa: O padrao de arquitetura orientada a eventos exige que cada lancamento
    /// aceito gere um evento de dominio duravel para consumo dos servicos a jusante.
    /// </summary>
    [Fact]
    public async Task DevePublicarEventoDeDominioAposGravacaoDoCredito()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_CRED_003";
        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 1200.50m,
            Type: "Credit",
            Description: "Entrada por Pix"
        );

        // Acao
        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);

        // Assercoes sobre a publicacao do evento no barramento
        var eventos = _ambiente.ObterEventosPublicados();
        eventos.Should().Contain(e =>
            e.TransactionId == lancamento!.TransactionId &&
            e.MerchantId == comercianteId &&
            e.Amount == 1200.50m &&
            e.Type == "Credit"
        );
    }

    /// <summary>
    /// Cenario: Persistencia e integridade de caracteres do idioma portugues com acentos.
    /// Justificativa: Assegura suporte estrito a acentuacao grafica (ç, ã, é, ó) sem
    /// corromper a codificacao UTF-8 em todo o pipeline HTTP e de persistencia.
    /// </summary>
    [Fact]
    public async Task DevePersistirDescricaoEmPortuguesComAcentuacaoCorreta()
    {
        // Arranjo
        var descricaoComAcentos = "Liquidação de títulos de crédito e prestação de serviços farmacêuticos";
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_CRED_004",
            Amount: 380.20m,
            Type: "Credit",
            Description: descricaoComAcentos
        );

        // Acao
        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);

        // Assercao
        lancamento.Should().NotBeNull();
        var eventos = _ambiente.ObterEventosPublicados();
        var eventoPublicado = eventos.FirstOrDefault(e => e.TransactionId == lancamento!.TransactionId);
        eventoPublicado.Should().NotBeNull();
        eventoPublicado!.Description.Should().Be(descricaoComAcentos);
    }

    /// <summary>
    /// Cenario: Suporte a valores fracionados com alta precisao monetaria de centavos.
    /// Justificativa: Evita problemas de arredondamento de ponto flutuante, garantindo
    /// calculo aritmetico decimal exato no saldo acumulado.
    /// </summary>
    [Fact]
    public async Task DeveSuportarLancamentosDeCreditoFracionadosComCentavos()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_CRED_005";
        var valor1 = 0.01m;
        var valor2 = 99.99m;

        // Acao: Registra dois lancamentos com valores fracionados
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, valor1, "Credit", "Taxa minima"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, valor2, "Credit", "Venda complementar"));

        // Assercao no consolidado
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.TotalCredits.Should().Be(100.00m, "a soma exata de 0.01 com 99.99 deve ser exatamente 100.00");
        consolidado.ClosingBalance.Should().Be(100.00m);
        consolidado.TransactionCount.Should().Be(2);
    }
}
