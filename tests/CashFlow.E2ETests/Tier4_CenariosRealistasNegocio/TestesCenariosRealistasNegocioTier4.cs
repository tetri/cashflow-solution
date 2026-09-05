using System.Diagnostics;
using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier4_CenariosRealistasNegocio;

/// <summary>
/// Suite de testes E2E do Tier 4 para Cenarios Realistas de Negocio (Real-World Application Scenarios).
/// Modela e valida fluxos de alta complexidade do dia a dia operacional:
/// - Dia comercial completo de varejo com concorrencia massiva
/// - Resiliencia e recuperacao transparente de infraestrutura sob queda do Redis
/// - Protecao do barramento contra mensagens venenosas direcionadas a DLQ
/// - Teste de carga e vazao em consultas de consolidado (> 50 RPS)
/// - Tempestade de retentativas de rede com idempotencia estrita
/// </summary>
public class TestesCenariosRealistasNegocioTier4 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesCenariosRealistasNegocioTier4()
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
    /// Cenario 1 (Tier 4): Dia comercial de varejo completo com multiplas vendas e estornos concorrentes.
    /// Funcionalidades Exercitadas: F1 (Credito), F2 (Debito), F3 (Idempotencia), F4 (Consolidado Cache), F6 (Consumo Idempotente).
    /// Complexidade: Alta.
    /// Justificativa: Simula o expediente completo de uma grande rede de varejo:
    /// - 50 vendas (creditos) simultaneas originadas de multiplos caixas PDV
    /// - 10 cancelamentos/estornos (debitos) concorrentes
    /// - Todas as operacoes com chaves unicas de idempotencia
    /// - Consolidado final deve bater perfeitamente a soma aritmetica no centavo.
    /// </summary>
    [Fact]
    public async Task Cenario1_DiaComercialDeVarejoComMultiplasVendasEEstornosConcorrentes()
    {
        // Arranjo
        var comercianteId = "SUPERMERCADO_CENTRAL_01";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // 50 vendas com valores variados (R$ 10,00 ate R$ 108,00)
        var vendas = Enumerable.Range(1, 50)
            .Select(i => new RequisicaoLancamento(
                MerchantId: comercianteId,
                Amount: 10.00m + (i * 2.00m),
                Type: "Credit",
                Description: $"Venda PDV {i}",
                IdempotencyKey: $"VENDA-PDV-{i}-{Guid.NewGuid()}"
            ))
            .ToArray();

        // 10 estornos de vendas canceladas (R$ 15,00 ate R$ 60,00)
        var estornos = Enumerable.Range(1, 10)
            .Select(i => new RequisicaoLancamento(
                MerchantId: comercianteId,
                Amount: 15.00m + (i * 5.00m),
                Type: "Debit",
                Description: $"Estorno PDV {i}",
                IdempotencyKey: $"ESTORNO-PDV-{i}-{Guid.NewGuid()}"
            ))
            .ToArray();

        var totalCreditosEsperado = vendas.Sum(v => v.Amount);
        var totalDebitosEsperado = estornos.Sum(e => e.Amount);
        var saldoFechamentoEsperado = totalCreditosEsperado - totalDebitosEsperado;

        // Acao: Execucao concorrente massiva simulando caixas operando em paralelo
        var todasOperacoes = vendas.Concat(estornos).OrderBy(_ => Guid.NewGuid()).ToArray();
        var tarefas = todasOperacoes
            .Select(op => _cliente.RegistrarLancamentoAsync(op))
            .ToArray();

        var resultadosLancamentos = await Task.WhenAll(tarefas);

        // Assercoes nos lancamentos
        resultadosLancamentos.Should().HaveCount(60);
        resultadosLancamentos.Should().AllSatisfy(l => l.Should().NotBeNull());

        // Assercoes no consolidado diario
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.Cached.Should().BeTrue("ao fim do dia comercial, o saldo consolidado deve estar aquecido no cache");
        consolidado.TotalCredits.Should().Be(totalCreditosEsperado, "o total de creditos deve somar exatamente as 50 vendas");
        consolidado.TotalDebits.Should().Be(totalDebitosEsperado, "o total de debitos deve somar exatamente os 10 estornos");
        consolidado.ClosingBalance.Should().Be(saldoFechamentoEsperado, "o saldo final deve ser creditos menos debitos com exatidao");
        consolidado.TransactionCount.Should().Be(60, "deve totalizar exatamente 60 transacoes registradas no dia");
    }

    /// <summary>
    /// Cenario 2 (Tier 4): Queda e recuperacao do Redis com verificacao de fallback transparente.
    /// Funcionalidades Exercitadas: F4 (Consolidado Cache Hit), F5 (Consolidado Fallback).
    /// Complexidade: Alta.
    /// Justificativa: Simula um incidente real de infraestrutura onde o Redis fica temporariamente
    /// inoperante sob trafego ativo e posteriormente se recupera sem perda de dados.
    /// </summary>
    [Fact]
    public async Task Cenario2_QuedaERecuperacaoDoRedisComVerificacaoDeFallbackTransparente()
    {
        // Arranjo: Cria vendas iniciais sob operacao nominal
        var comercianteId = "LOJA_ELETRONICOS_02";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 1500m, "Credit", "Venda de Smartphone"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 300m, "Debit", "Frete de entrega"));

        // Fase 1: Consulta nominal em cache aquecido
        var consolidadoFase1 = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidadoFase1!.Cached.Should().BeTrue();
        consolidadoFase1.ClosingBalance.Should().Be(1200m);

        // Fase 2: Simulacao de queda abrupta do cluster Redis
        _ambiente.SimularFalhaRedis(true);

        // Clientes continuam consultando o saldo via fallback PostgreSQL
        var consolidadoFase2 = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidadoFase2!.Cached.Should().BeFalse("deve indicar fallback relacional durante a falha do Redis");
        consolidadoFase2.ClosingBalance.Should().Be(1200m, "o saldo deve manter consistencia absoluta sob fallback");

        // Enquanto o Redis esta fora, novas transacoes continuam chegando normalmente
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 800m, "Credit", "Venda de Fone de Ouvido"));
        var consolidadoFase2Atualizado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidadoFase2Atualizado!.ClosingBalance.Should().Be(2000m, "1200 + 800");

        // Fase 3: Recuperacao do cluster Redis
        _ambiente.SimularFalhaRedis(false);

        // Consulta pos-recuperacao: deve reabastecer o cache e responder com cached: true
        var consolidadoFase3 = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidadoFase3!.Cached.Should().BeTrue("o cache deve ser restaurado apos a volta do servico Redis");
        consolidadoFase3.ClosingBalance.Should().Be(2000m);
        consolidadoFase3.TransactionCount.Should().Be(3);
    }

    /// <summary>
    /// Cenario 3 (Tier 4): Ingestao de lote de mensagens venenosas e direcionamento para DLQ.
    /// Funcionalidades Exercitadas: F6 (Consumo Idempotente & DLQ).
    /// Complexidade: Media.
    /// Justificativa: Comprova que uma rajada de mensagens toxicas ou corrompidas e isolada na DLQ
    /// sem causar starvation nem indisponibilizar o processamento das transacoes legitimas.
    /// </summary>
    [Fact]
    public async Task Cenario3_IngestaoDeMensagensVenenosasEDirecionamentoParaDlq()
    {
        // Arranjo
        var comercianteId = "FARMACIA_24HORAS_03";
        var totalVenenos = 15;

        // Acao 1: Injeta lote de 15 mensagens venenosas (JSON corrompido, campos faltantes, valores nulos)
        for (var i = 1; i <= totalVenenos; i++)
        {
            _ambiente.InjetarMensagemVenenoNoWorker($"{{ \"mensagem_toxica\": {i}, \"status\": \"corrompida\" ");
        }

        // Assercoes na DLQ
        _ambiente.ObterContagemFilaDlq().Should().Be(totalVenenos, "todas as 15 mensagens invalidas devem estar isoladas na DLQ");

        // Acao 2: Ingestao imediata de 10 transacoes legitimas pelo pipeline oficial
        for (var i = 1; i <= 10; i++)
        {
            await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
                MerchantId: comercianteId,
                Amount: 50.00m,
                Type: "Credit",
                Description: $"Venda de Medicamento {i}"
            ));
        }

        // Assercoes no saldo consolidado
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.TotalCredits.Should().Be(500.00m, "as 10 vendas legitimas de 50.00 devem ser computadas integralmente");
        consolidado.TransactionCount.Should().Be(10);
        _ambiente.ObterContagemFilaDlq().Should().Be(totalVenenos, "a contagem da DLQ deve permanecer estritamente em 15");
    }

    /// <summary>
    /// Cenario 4 (Tier 4): Picos de consulta de consolidado garantindo alta capacidade e latencia reduzida.
    /// Funcionalidades Exercitadas: F4 (Consolidado Cache Hit).
    /// Complexidade: Alta.
    /// Justificativa: Valida a capacidade de atender rajadas intensas de leitura de saldo
    /// simulando relatorios gerenciais em horario de fechamento (garantindo latencia em nivel de cache).
    /// </summary>
    [Fact]
    public async Task Cenario4_PicosDeConsultaDeConsolidadoGarantindoAltaCapacidadeELatenciaReduzida()
    {
        // Arranjo: Prepara massa de dados de teste
        var comercianteId = "POSTO_COMBUSTIVEL_04";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 4500m, "Credit", "Fechamento de Pista 1"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 1200m, "Debit", "Pagamento de Frete Tanque"));

        // Aquecimento previo de cache
        await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Acao: Rajada de 100 consultas paralelas simultaneas simulando pico de acessos
        var totalConsultas = 100;
        var cronometroGeral = Stopwatch.StartNew();

        var tarefas = Enumerable.Range(0, totalConsultas)
            .Select(_ => _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje))
            .ToArray();

        var respostas = await Task.WhenAll(tarefas);
        cronometroGeral.Stop();

        // Assercoes
        respostas.Should().HaveCount(totalConsultas);
        respostas.Should().AllSatisfy(r =>
        {
            r.Should().NotBeNull();
            r!.Cached.Should().BeTrue("todas as requisicoes da rajada devem ser atendidas via cache Redis");
            r.ClosingBalance.Should().Be(3300m);
        });

        // Validacao de desempenho: 100 consultas em memoria devem ser concluidas em tempo reduzido
        cronometroGeral.ElapsedMilliseconds.Should().BeLessThan(2000, "100 consultas em cache devem executar rapidamente");
    }

    /// <summary>
    /// Cenario 5 (Tier 4): Repeticao massiva de transacoes com a mesma IdempotencyKey (tempestade de rede).
    /// Funcionalidades Exercitadas: F1 (Credito), F3 (Idempotencia).
    /// Complexidade: Media.
    /// Justificativa: Simula uma particao intermitente de rede de conexao celular de um terminal POS
    /// que reenvia o mesmo lancamento 50 vezes em paralelo; exatamente 1 lancamento deve ser criado
    /// e todas as outras 49 requisicoes devem retornar sucesso idempotente sem duplicar o saldo.
    /// </summary>
    [Fact]
    public async Task Cenario5_RepeticaoMassivaDeTransacoesComAMesmaIdempotencyKey()
    {
        // Arranjo
        var comercianteId = "RESTAURANTE_GOURMET_05";
        var chaveIdempotenciaUnica = $"CHAVE-POS-DISCONNECT-{Guid.NewGuid()}";
        var valorVenda = 385.50m;

        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: valorVenda,
            Type: "Credit",
            Description: "Jantar corporativo mesa 12",
            IdempotencyKey: chaveIdempotenciaUnica
        );

        // Acao: 50 reenvios simultaneos concorrentes com a mesmissima chave
        var totalReenvios = 50;
        var tarefas = Enumerable.Range(0, totalReenvios)
            .Select(_ => _cliente.RegistrarLancamentoBrutoAsync(requisicao))
            .ToArray();

        var respostas = await Task.WhenAll(tarefas);

        // Assercoes nas respostas HTTP
        respostas.Should().HaveCount(totalReenvios);
        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK);

        var criacoes = respostas.Count(r => r.StatusCode == HttpStatusCode.Created);
        var idempotentes = respostas.Count(r => r.StatusCode == HttpStatusCode.OK);

        criacoes.Should().Be(1, "estritamente uma chamada deve criar o recurso novo (201 Created)");
        idempotentes.Should().Be(49, "as outras 49 requisicoes repetidas devem retornar 200 OK com os dados existentes");

        // Assercoes de integridade no saldo consolidado
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.TotalCredits.Should().Be(valorVenda, "o valor de 385.50 deve ser contabilizado apenas uma unica vez");
        consolidado.ClosingBalance.Should().Be(valorVenda);
        consolidado.TransactionCount.Should().Be(1, "a contagem de transacoes deve ser rigorosamente 1");

        // Assercoes no barramento assincrono
        var eventos = _ambiente.ObterEventosPublicados();
        eventos.Count(e => e.MerchantId == comercianteId).Should().Be(1, "apenas 1 evento assincrono de transacao criada deve ser publicado");
    }
}
