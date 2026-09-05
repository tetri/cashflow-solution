using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier1_CoberturaFuncionalidades;

/// <summary>
/// Suite de testes E2E do Tier 1 para a funcionalidade de Consolidado Diario (Fallback).
/// Valida a alta disponibilidade do servico de leitura, assegurando que, sob indisponibilidade,
/// timeout ou particao do cache Redis, as requisicoes sejam redirecionadas com transparencia
/// para o banco relacional PostgreSQL sem expor falhas ao usuario final.
/// </summary>
public class TestesConsolidadoDiarioFallbackTier1 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesConsolidadoDiarioFallbackTier1()
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
    /// Cenario: Falha total simulada no Redis e verificacao de fallback automatico.
    /// Justificativa: Garante que a API execute a politica de resiliencia (Polly / circuit breaker)
    /// e sirva os dados diretamente do PostgreSQL sem retornar erro 500 ao cliente.
    /// </summary>
    [Fact]
    public async Task DeveRealizarFallbackTransparenteParaPostgreSQLQuandoRedisFalhar()
    {
        // Arranjo: Cria lancamento no banco relacional
        var comercianteId = "COMERCIANTE_FALLBACK_001";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 650.00m,
            Type: "Credit",
            Description: "Entrada comercial de teste de fallback"
        ));

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao: Simula falha/queda do Redis
        _ambiente.SimularFalhaRedis(true);

        var respostaHttp = await _cliente.ConsultarConsolidadoBrutoAsync(comercianteId, dataHoje);

        // Assercoes
        respostaHttp.StatusCode.Should().Be(HttpStatusCode.OK, "a API deve degradar graciosamente servindo a partir do banco relacional");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.TotalCredits.Should().Be(650.00m);
        consolidado.ClosingBalance.Should().Be(650.00m);
    }

    /// <summary>
    /// Cenario: Indicador 'cached' deve ser false durante execucao sob fallback.
    /// Justificativa: Permite auditoria operacional de observabilidade para identificar
    /// se o dado veio da camada de cache rapida ou da camada relacional de persistencia.
    /// </summary>
    [Fact]
    public async Task DeveRetornarIndicadorCachedFalsoDuranteFalhaDoRedis()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_FALLBACK_002";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 320m, "Credit", "Venda"));
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao: Ativa falha de cache
        _ambiente.SimularFalhaRedis(true);
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Assercao
        consolidado.Should().NotBeNull();
        consolidado!.Cached.Should().BeFalse("o indicador cached deve ser falso quando atendido pela camada de fallback");
    }

    /// <summary>
    /// Cenario: Consistencia aritmetica dos valores retornados via fallback relacional.
    /// Justificativa: Comprova que os dados gravados no PostgreSQL pelo worker preservam
    /// a integridade matematica de creditos, debitos e saldo liquido.
    /// </summary>
    [Fact]
    public async Task DeveManterConsistenciaDeSaldoNoFallbackAposQuedaDoCache()
    {
        // Arranjo: Varios creditos e debitos
        var comercianteId = "COMERCIANTE_FALLBACK_003";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 1000m, "Credit", "Recebimento 1"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 250m, "Debit", "Pagamento 1"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 150m, "Debit", "Pagamento 2"));

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao
        _ambiente.SimularFalhaRedis(true);
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Assercoes
        consolidado!.TotalCredits.Should().Be(1000m);
        consolidado.TotalDebits.Should().Be(400m);
        consolidado.ClosingBalance.Should().Be(600m, "1000 - 400 deve resultar em exatamente 600");
        consolidado.TransactionCount.Should().Be(3);
    }

    /// <summary>
    /// Cenario: Auto-recuperacao e reabastecimento do cache quando o Redis se recupera.
    /// Justificativa: Quando a conexao com o Redis e restaurada, leituras subsequentes
    /// devem popular o cache e voltar a responder com 'cached: true'.
    /// </summary>
    [Fact]
    public async Task DeveRestabelecerCacheAutomaticamenteAposRecuperacaoDoRedis()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_FALLBACK_004";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 500m, "Credit", "Carga"));
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Passo 1: Falha do Redis -> Leitura via Fallback
        _ambiente.SimularFalhaRedis(true);
        var leituraFallback = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        leituraFallback!.Cached.Should().BeFalse();

        // Passo 2: Recuperacao do Redis
        _ambiente.SimularFalhaRedis(false);

        // Passo 3: Nova leitura deve usar ou recompor o cache
        var leituraPosRecuperacao = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Assercoes
        leituraPosRecuperacao!.Cached.Should().BeTrue("apos a recuperacao do Redis, o cache volta a responder");
        leituraPosRecuperacao.ClosingBalance.Should().Be(500m);
    }

    /// <summary>
    /// Cenario: Fallback sob consulta de comerciante inexistente.
    /// Justificativa: Deve retornar 200 OK com saldo zerado mesmo na vigencia de queda do Redis.
    /// </summary>
    [Fact]
    public async Task DeveRetornarSaldoZeradoNoFallbackQuandoNaoHouverDadosNoPostgres()
    {
        // Arranjo
        var comercianteInexistente = "COMERCIANTE_INEXISTENTE_FALLBACK_005";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao
        _ambiente.SimularFalhaRedis(true);
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteInexistente, dataHoje);

        // Assercoes
        consolidado.Should().NotBeNull();
        consolidado!.ClosingBalance.Should().Be(0m);
        consolidado.TransactionCount.Should().Be(0);
        consolidado.Cached.Should().BeFalse();
    }
}
