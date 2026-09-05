using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier2_CasosDeBordaELimites;

/// <summary>
/// Suite de testes E2E do Tier 2 para Casos de Borda e Limites em Consolidado Diario (Fallback).
/// Valida condicoes de estresse durante indisponibilidade do Redis, alta concorrencia sob fallback,
/// intermitencia rapida de conectividade (flapping) e consistencia sob grandes volumes de dados.
/// </summary>
public class TestesBordaConsolidadoFallbackTier2 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesBordaConsolidadoFallbackTier2()
    {
        _ambiente = new AmbienteTesteE2E();
        _cliente = new ClienteApiFluxoCaixa(_ambiente.CriarClienteHttp());
    }

    public void Dispose()
    {
        _ambiente.LimparEstado();
    }

    /// <summary>
    /// Cenario: Multiplas consultas concorrentes simultaneas sob queda total do Redis.
    /// Justificativa: Sob falha do cache, o banco de dados e a camada de resiliencia
    /// devem suportar disparos paralelos sem deadlocks ou degradacao catastrofica.
    /// </summary>
    [Fact]
    public async Task DeveSuportarConsultasConcorrentesDuranteQuedaDoRedis()
    {
        // Arranjo: Registra lancamento previo
        var comercianteId = "COMERCIANTE_BORDA_FALLBACK_001";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 1500m, "Credit", "Carga previa"));
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao: Derruba o Redis e dispara 15 consultas paralelas
        _ambiente.SimularFalhaRedis(true);

        var tarefas = Enumerable.Range(0, 15)
            .Select(_ => _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje))
            .ToArray();

        var resultados = await Task.WhenAll(tarefas);

        // Assercoes
        resultados.Should().AllSatisfy(r =>
        {
            r.Should().NotBeNull();
            r!.Cached.Should().BeFalse();
            r.ClosingBalance.Should().Be(1500m);
        });
    }

    /// <summary>
    /// Cenario: Intermitencia rapida de conexao com o Redis (flapping).
    /// Justificativa: Simula oscilacoes de rede onde o cache fica disponivel e indisponivel em intervalos curtos;
    /// o servico deve alternar dinamicamente entre cache e fallback sem travar.
    /// </summary>
    [Fact]
    public async Task DeveTratarIntermitenciaRapidaFlappingEntreCacheEFallback()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_BORDA_FALLBACK_002";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 700m, "Credit", "Venda"));
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao: Alterna 6 vezes o estado do Redis realizando consultas intermediarias
        for (var i = 0; i < 6; i++)
        {
            var simularFalha = i % 2 == 0;
            _ambiente.SimularFalhaRedis(simularFalha);

            var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
            consolidado.Should().NotBeNull();
            consolidado!.ClosingBalance.Should().Be(700m);
            consolidado.Cached.Should().Be(!simularFalha, $"quando a falha e {simularFalha}, o cache deve ser {!simularFalha}");
        }
    }

    /// <summary>
    /// Cenario: Novos lancamentos ocorrendo enquanto o Redis esta completamente fora do ar.
    /// Justificativa: Garante que o worker persista corretamente no PostgreSQL mesmo se a escrita
    /// no Redis falhar (resiliencia write-through com degradacao graciosa).
    /// </summary>
    [Fact]
    public async Task DeveGarantirConsistenciaDeDadosGravadosDurantePeriodoDeFalhaDoRedis()
    {
        // Arranjo: Redis em falha ANTES dos lancamentos
        _ambiente.SimularFalhaRedis(true);
        var comercianteId = "COMERCIANTE_BORDA_FALLBACK_003";

        // Acao: Registra credito e debito durante a indisponibilidade do cache
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 1200m, "Credit", "Entrada sem Redis"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 450m, "Debit", "Saida sem Redis"));

        // Assercoes via fallback
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado!.Cached.Should().BeFalse();
        consolidado.ClosingBalance.Should().Be(750m, "1200 - 450 deve ser gravado perfeitamente no PostgreSQL");
        consolidado.TransactionCount.Should().Be(2);
    }

    /// <summary>
    /// Cenario: Consulta para comerciante sem lancamentos em data arbitraria sob falha do Redis.
    /// Justificativa: Nao deve resultar em erro 500 nem null reference; retorna 200 OK com saldo zerado.
    /// </summary>
    [Fact]
    public async Task DeveRetornarStatus200ParaDataInexistenteDuranteFalhaDoRedis()
    {
        // Arranjo
        _ambiente.SimularFalhaRedis(true);
        var comercianteInexistente = "COMERCIANTE_FANTASMA_004";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao
        var respostaHttp = await _cliente.ConsultarConsolidadoBrutoAsync(comercianteInexistente, dataHoje);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.OK);
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteInexistente, dataHoje);
        consolidado!.ClosingBalance.Should().Be(0m);
        consolidado.Cached.Should().BeFalse();
    }

    /// <summary>
    /// Cenario: Recuperacao e consulta com volume acumulado de movimentacoes no PostgreSQL.
    /// Justificativa: Comprova que o agregador relacional e a camada de mapeamento respondem
    /// com precisao quando ha dezenas de operacoes somadas.
    /// </summary>
    [Fact]
    public async Task DeveProcessarFallbackComGrandeVolumeDeLancamentosSemErros()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_BORDA_FALLBACK_005";
        var totalOperacoes = 20;

        for (var i = 1; i <= totalOperacoes; i++)
        {
            await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
                MerchantId: comercianteId,
                Amount: 50.00m,
                Type: "Credit",
                Description: $"Lote {i}"
            ));
        }

        // Acao: Desativa Redis e consulta
        _ambiente.SimularFalhaRedis(true);
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Assercoes
        consolidado!.TotalCredits.Should().Be(1000.00m, "20 vezes 50.00 deve totalizar 1000.00");
        consolidado.ClosingBalance.Should().Be(1000.00m);
        consolidado.TransactionCount.Should().Be(totalOperacoes);
    }
}
