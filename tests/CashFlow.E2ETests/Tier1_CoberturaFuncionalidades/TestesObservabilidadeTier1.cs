using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier1_CoberturaFuncionalidades;

/// <summary>
/// Testes automatizados de observabilidade e monitoramento (Tier 1).
/// Valida a exposicao publica das sondas de Liveness, Readiness e do endpoint de metricas Prometheus,
/// assegurando ausencia de necessidade de autenticacao e integridade dos dados expostos.
/// </summary>
public class TestesObservabilidadeTier1 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly HttpClient _clienteHttp;

    public TestesObservabilidadeTier1()
    {
        _ambiente = new AmbienteTesteE2E();
        _clienteHttp = _ambiente.CriarClienteHttp();
    }

    public void Dispose()
    {
        _clienteHttp.Dispose();
        _ambiente.LimparEstado();
        _ambiente.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HealthLive_SemAutenticacao_DeveRetornarHttp200OkEServicoAtivo()
    {
        // Act: requisicao publica a sonda de vivacidade (Liveness Probe)
        var resposta = await _clienteHttp.GetAsync("/health/live");

        // Assert: HTTP 200 OK sem necessidade de cabecalho de autenticacao
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var conteudo = await resposta.Content.ReadAsStringAsync();
        conteudo.Should().Contain("Saudavel");
        conteudo.Should().Contain("Liveness");
    }

    [Fact]
    public async Task HealthReady_SemAutenticacao_DeveRetornarHttp200OkEStatusSaudavel()
    {
        // Act: requisicao publica a sonda de prontidao (Readiness Probe)
        var resposta = await _clienteHttp.GetAsync("/health/ready");

        // Assert: HTTP 200 OK com diagnostico de dependencias
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var conteudo = await resposta.Content.ReadAsStringAsync();
        conteudo.Should().Contain("Saudavel");
        conteudo.Should().Contain("PostgreSQL");
    }

    [Fact]
    public async Task Health_ComDegradacaoDeDependencia_DeveIndicarStatusDegradado()
    {
        // Arrange: simulacao de indisponibilidade de dependencia
        _ambiente.SimularFalhaRedis(true);

        // Act: consulta a saude operacional
        var resposta = await _clienteHttp.GetAsync("/health");

        // Assert: status geral indica degradacao operacional
        var conteudo = await resposta.Content.ReadAsStringAsync();
        conteudo.Should().Contain("Degradado");
        conteudo.Should().Contain("Indisponivel");
    }

    [Fact]
    public async Task Metrics_SemAutenticacao_DeveRetornarHttp200OkEFormatoPrometheus()
    {
        // Act: requisicao publica de scraping para metricas Prometheus
        var resposta = await _clienteHttp.GetAsync("/metrics");

        // Assert: HTTP 200 OK com payload no formato texto canônico do Prometheus
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var conteudo = await resposta.Content.ReadAsStringAsync();
        conteudo.Should().Contain("# HELP");
        conteudo.Should().Contain("# TYPE");
        conteudo.Should().Contain("cashflow_transactions_created_total");
    }
}
