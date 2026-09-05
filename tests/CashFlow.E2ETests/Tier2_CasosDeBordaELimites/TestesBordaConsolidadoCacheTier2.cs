using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier2_CasosDeBordaELimites;

/// <summary>
/// Suite de testes E2E do Tier 2 para Casos de Borda e Limites em Consolidado Diario (Cache Hit).
/// Valida integridade em datas atipicas (passado distante, futuro, formatos invalidos),
/// isolamento na virada de dia e compatibilidade de identificadores com caracteres especiais.
/// </summary>
public class TestesBordaConsolidadoCacheTier2 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesBordaConsolidadoCacheTier2()
    {
        _ambiente = new AmbienteTesteE2E();
        _cliente = new ClienteApiFluxoCaixa(_ambiente.CriarClienteHttp());
    }

    public void Dispose()
    {
        _ambiente.LimparEstado();
    }

    /// <summary>
    /// Cenario: Consulta de saldo consolidado em data futura distante (ex.: ano 2099).
    /// Justificativa: Nao deve quebrar o parser de data nem o Redis, retornando 200 OK com saldo zerado.
    /// </summary>
    [Fact]
    public async Task DeveRetornarConsolidadoZeradoParaDataFuturaDistante()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_BORDA_CACHE_001";
        var dataFutura = "2099-12-31";

        // Acao
        var respostaHttp = await _cliente.ConsultarConsolidadoBrutoAsync(comercianteId, dataFutura);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.OK);
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataFutura);
        consolidado!.TotalCredits.Should().Be(0m);
        consolidado.TotalDebits.Should().Be(0m);
        consolidado.ClosingBalance.Should().Be(0m);
    }

    /// <summary>
    /// Cenario: Consulta de saldo em data passada distante (ex.: ano 2000).
    /// Justificativa: Garante resiliencia para consultas retroativas sem registros.
    /// </summary>
    [Fact]
    public async Task DeveRetornarConsolidadoZeradoParaDataPassadaDistante()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_BORDA_CACHE_002";
        var dataPassada = "2000-01-01";

        // Acao
        var respostaHttp = await _cliente.ConsultarConsolidadoBrutoAsync(comercianteId, dataPassada);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.OK);
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataPassada);
        consolidado!.ClosingBalance.Should().Be(0m);
    }

    /// <summary>
    /// Cenario: Consulta com formato de data invalido (ex.: formato brasileiro dd/MM/yyyy ou texto livre).
    /// Justificativa: A API exige estritamente o padrao ISO 8601 (yyyy-MM-dd) e deve rejeitar
    /// formatos divergentes com HTTP 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task DeveRejeitarFormatoDeDataInvalidoRetornandoErro400()
    {
        // Arranjo: Formato numerico com meses/dias invalidos
        var dataInvalida = "2026-99-99";
        var comercianteId = "COMERCIANTE_BORDA_CACHE_003";

        // Acao
        var respostaHttp = await _cliente.ConsultarConsolidadoBrutoAsync(comercianteId, dataInvalida);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaHttp);
        detalhes!.Detail.Should().Contain("yyyy-MM-dd");
    }

    /// <summary>
    /// Cenario: Isolamento estrito de datas consecutivas (virada de meia-noite).
    /// Justificativa: Comprova que um saldo consolidado de um dia nao vaze nem interfira
    /// nas metricas do dia imediatamente subsequente.
    /// </summary>
    [Fact]
    public async Task DeveIsolarConsolidadosEmDatasConsecutivasNaViradaDoDia()
    {
        // Arranjo: Registra lancamento para o dia corrente
        var comercianteId = "COMERCIANTE_BORDA_CACHE_004";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var dataAmanha = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd");

        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 450m, "Credit", "Venda de hoje"));

        // Acao: Consulta hoje e amanha
        var consolidadoHoje = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        var consolidadoAmanha = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataAmanha);

        // Assercoes
        consolidadoHoje!.ClosingBalance.Should().Be(450m);
        consolidadoHoje.TransactionCount.Should().Be(1);

        consolidadoAmanha!.ClosingBalance.Should().Be(0m, "a data de amanha nao possui movimentacoes ainda");
        consolidadoAmanha.TransactionCount.Should().Be(0);
    }

    /// <summary>
    /// Cenario: Identificador de comerciante contendo caracteres validos como hifens, pontos e sublinhados.
    /// Justificativa: Chaves de cache com separadores de roteamento nao devem quebrar o namespace Redis.
    /// </summary>
    [Fact]
    public async Task DeveSuportarIdentificadorDeComercianteComHifensEUnderlinesEmCache()
    {
        // Arranjo
        var comercianteId = "LOJA-FILIAL_01.SP-CENTRO";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 275m, "Credit", "Venda em filial"));

        // Acao
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Assercoes
        consolidado.Should().NotBeNull();
        consolidado!.MerchantId.Should().Be(comercianteId);
        consolidado.ClosingBalance.Should().Be(275m);
        consolidado.Cached.Should().BeTrue();
    }
}
