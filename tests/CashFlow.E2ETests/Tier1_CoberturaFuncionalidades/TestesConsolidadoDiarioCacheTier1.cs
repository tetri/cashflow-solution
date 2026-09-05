using System.Diagnostics;
using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier1_CoberturaFuncionalidades;

/// <summary>
/// Suite de testes E2E do Tier 1 para a funcionalidade de Consolidado Diario (Cache Hit).
/// Valida a entrega de consultas de alta performance via Redis com indicador de cache ativo,
/// calculo correto de saldo diario e isolamento por comerciante e data.
/// </summary>
public class TestesConsolidadoDiarioCacheTier1 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesConsolidadoDiarioCacheTier1()
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
    /// Cenario: Consulta de consolidado diario apos processamento de lancamento.
    /// Justificativa: Garante que o worker atualize o Redis (padrao write-through)
    /// e a API responda com a flag 'cached: true'.
    /// </summary>
    [Fact]
    public async Task DeveRetornarConsolidadoComIndicadorCachedVerdadeiroAposLancamento()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_CACHE_001";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 300.00m,
            Type: "Credit",
            Description: "Recebimento comercial em dinheiro"
        ));

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao
        var respostaHttp = await _cliente.ConsultarConsolidadoBrutoAsync(comercianteId, dataHoje);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.OK);
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.MerchantId.Should().Be(comercianteId);
        consolidado.Cached.Should().BeTrue("o consolidado deve ser servido a partir do cache Redis aquecido pelo worker");
        consolidado.TotalCredits.Should().Be(300.00m);
        consolidado.ClosingBalance.Should().Be(300.00m);
        consolidado.TransactionCount.Should().Be(1);
    }

    /// <summary>
    /// Cenario: Consulta para comerciante sem qualquer movimentacao financeira na data.
    /// Justificativa: O sistema deve retornar 200 OK com saldo zerado e contadores zerados,
    /// sem lancar excecao de registro inexistente.
    /// </summary>
    [Fact]
    public async Task DeveRetornarConsolidadoZeradoParaComercianteSemLancamentos()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_SEM_HISTORICO_002";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Assercoes
        consolidado.Should().NotBeNull();
        consolidado!.MerchantId.Should().Be(comercianteId);
        consolidado.TotalCredits.Should().Be(0m);
        consolidado.TotalDebits.Should().Be(0m);
        consolidado.ClosingBalance.Should().Be(0m);
        consolidado.TransactionCount.Should().Be(0);
    }

    /// <summary>
    /// Cenario: Avaliacao de latencia em consultas aquecidas em cache.
    /// Justificativa: O requisito R1 estipula que as leituras no Redis devem operar em
    /// latencia minima (menor que 5ms em ambiente nominal de producao).
    /// </summary>
    [Fact]
    public async Task DeveResponderConsultaEmCacheRapidamente()
    {
        // Arranjo: Cria lancamento e realiza aquecimento previo
        var comercianteId = "COMERCIANTE_CACHE_LATENCIA_003";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 100m, "Credit", "Aquecimento"));
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Execucao de aquecimento
        await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Acao: Medicao de tempo de execucao da consulta em cache quente
        var cronometro = Stopwatch.StartNew();
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        cronometro.Stop();

        // Assercoes
        consolidado!.Cached.Should().BeTrue();
        cronometro.ElapsedMilliseconds.Should().BeLessThan(100, "a resposta em memoria/cache deve ser sub-milissegundo ou ordens de magnitude menor que banco");
    }

    /// <summary>
    /// Cenario: Renovacao imediata do cache apos novo lancamento intercalado.
    /// Justificativa: Evita leituras desatualizadas (stale reads), garantindo consistencia eventual
    /// quase instantanea via padrao write-through no worker.
    /// </summary>
    [Fact]
    public async Task DeveAtualizarCacheImediatamenteAposNovoLancamentoViaWriteThrough()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_CACHE_004";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Primeiro lancamento
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 400.00m, "Credit", "Venda 1"));
        var primeiroConsolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        primeiroConsolidado!.ClosingBalance.Should().Be(400.00m);

        // Acao: Segundo lancamento
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 150.00m, "Debit", "Pagamento de conta"));

        // Consulta subsequente
        var segundoConsolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Assercoes
        segundoConsolidado!.Cached.Should().BeTrue();
        segundoConsolidado.ClosingBalance.Should().Be(250.00m, "o cache deve refletir 400 - 150 = 250 imediatamente");
        segundoConsolidado.TransactionCount.Should().Be(2);
    }

    /// <summary>
    /// Cenario: Isolamento estrito de dados em cache para multiplos comerciantes.
    /// Justificativa: Garante que as chaves de cache (ex.: consolidated:{merchantId}:{date})
    /// nao sofram colisao ou vazamento de informacao entre inquilinos (multi-tenancy).
    /// </summary>
    [Fact]
    public async Task DeveIsolarConsolidadosDeComerciantesDistintosEmCache()
    {
        // Arranjo
        var comercianteA = "COMERCIANTE_ISOLADO_A";
        var comercianteB = "COMERCIANTE_ISOLADO_B";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteA, 800m, "Credit", "Credito A"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteB, 200m, "Debit", "Debito B"));

        // Acao
        var consolidadoA = await _cliente.ConsultarConsolidadoAsync(comercianteA, dataHoje);
        var consolidadoB = await _cliente.ConsultarConsolidadoAsync(comercianteB, dataHoje);

        // Assercoes
        consolidadoA!.MerchantId.Should().Be(comercianteA);
        consolidadoA.ClosingBalance.Should().Be(800m);
        consolidadoA.TotalCredits.Should().Be(800m);
        consolidadoA.TotalDebits.Should().Be(0m);

        consolidadoB!.MerchantId.Should().Be(comercianteB);
        consolidadoB.ClosingBalance.Should().Be(-200m);
        consolidadoB.TotalCredits.Should().Be(0m);
        consolidadoB.TotalDebits.Should().Be(200m);
    }
}
