using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier3_CombinacoesInterFuncionalidades;

/// <summary>
/// Suite de testes E2E do Tier 3 para Combinacoes Inter-Funcionalidades (Pairwise Combinations).
/// Valida a interacao sinergica entre multiplas funcionalidades criticas do ecossistema:
/// Credito, Debito, Idempotencia, Cache Redis, Fallback PostgreSQL, Consumo assincrono com DLQ
/// e Politica institucional de pt-BR e Zero Emojis.
/// </summary>
public class TestesCombinacoesInterFuncionalidadesTier3 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesCombinacoesInterFuncionalidadesTier3()
    {
        _ambiente = new AmbienteTesteE2E();
        _cliente = new ClienteApiFluxoCaixa(_ambiente.CriarClienteHttp());
    }

    public void Dispose()
    {
        _ambiente.LimparEstado();
    }

    /// <summary>
    /// Combinacao: F1 (Credito) + F2 (Debito) + F4 (Consolidado Cache Hit).
    /// Cenario: Sequencia intercalada de creditos e debitos no mesmo dia comercial.
    /// Justificativa: Valida a aritmetica financeira acumulada (Creditos - Debitos = Saldo Fechamento)
    /// servida diretamente a partir do cache Redis aquecido.
    /// </summary>
    [Fact]
    public async Task Combinacao_CreditoSeguidoDeDebito_DeveCalcularSaldoLiquidoExato()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_PAR_001";
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao: Registra R$ 1000 de credito, R$ 250 de debito, R$ 500 de credito e R$ 150 de debito
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 1000m, "Credit", "Venda 1"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 250m, "Debit", "Despesa 1"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 500m, "Credit", "Venda 2"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 150m, "Debit", "Despesa 2"));

        // Assercoes
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.Cached.Should().BeTrue("deve ser servido do Redis em condicoes nominais");
        consolidado.TotalCredits.Should().Be(1500m, "1000 + 500");
        consolidado.TotalDebits.Should().Be(400m, "250 + 150");
        consolidado.ClosingBalance.Should().Be(1100m, "1500 - 400 = 1100");
        consolidado.TransactionCount.Should().Be(4);
    }

    /// <summary>
    /// Combinacao: F1 (Credito) + F3 (Idempotencia) com alternancia de canal (Corpo vs Cabecalho).
    /// Cenario: Primeira chamada envia a chave no corpo e a segunda no cabecalho HTTP.
    /// Justificativa: Garante a interoperabilidade entre canais de envio da chave de idempotencia,
    /// retornando o mesmo identificador sem duplicidade de saldo.
    /// </summary>
    [Fact]
    public async Task Combinacao_IdempotenciaComAlternanciaDeCanalCabecalhoECorpo_DeveReconhecerChaveUnica()
    {
        // Arranjo
        var chaveCompartilhada = Guid.NewGuid().ToString();
        var comercianteId = "COMERCIANTE_PAR_002";
        var requisicaoCorpo = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 380m,
            Type: "Credit",
            Description: "Venda com chave no corpo",
            IdempotencyKey: chaveCompartilhada
        );

        var requisicaoCabecalho = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 380m,
            Type: "Credit",
            Description: "Venda com chave no corpo",
            IdempotencyKey: null // Chave sera injetada no cabecalho
        );

        // Acao
        var resposta1 = await _cliente.RegistrarLancamentoBrutoAsync(requisicaoCorpo);
        resposta1.StatusCode.Should().Be(HttpStatusCode.Created);
        var dados1 = await _cliente.RegistrarLancamentoAsync(requisicaoCorpo);

        var resposta2 = await _cliente.RegistrarLancamentoBrutoAsync(requisicaoCabecalho, chaveCompartilhada);
        resposta2.StatusCode.Should().Be(HttpStatusCode.OK);
        var dados2 = await _cliente.RegistrarLancamentoAsync(requisicaoCorpo);

        // Assercoes
        dados2!.TransactionId.Should().Be(dados1!.TransactionId);

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidado!.TransactionCount.Should().Be(1);
        consolidado.TotalCredits.Should().Be(380m);
    }

    /// <summary>
    /// Combinacao: F1 (Credito) + F5 (Consolidado Fallback).
    /// Cenario: Registro de lancamento seguido de consulta enquanto o Redis se encontra inoperante.
    /// Justificativa: Comprova que o fluxo de ponta a ponta sobrevive a indisponibilidade do Redis,
    /// persistindo no Postgres e servindo a leitura via fallback relacional.
    /// </summary>
    [Fact]
    public async Task Combinacao_CreditoSobFalhaDoRedis_DevePersistirNoPostgresEPermitirConsultaFallback()
    {
        // Arranjo: Redis com falha ativa
        _ambiente.SimularFalhaRedis(true);
        var comercianteId = "COMERCIANTE_PAR_003";

        // Acao: Registra credito de R$ 920,00
        var lancamento = await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 920.00m,
            Type: "Credit",
            Description: "Credito sob queda do Redis"
        ));

        // Consulta de saldo
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        // Assercoes
        lancamento.Should().NotBeNull();
        consolidado!.Cached.Should().BeFalse("deve indicar fallback para Postgres");
        consolidado.TotalCredits.Should().Be(920.00m);
        consolidado.ClosingBalance.Should().Be(920.00m);
    }

    /// <summary>
    /// Combinacao: F2 (Debito) + F3 (Idempotencia) + F4 (Consolidado Cache) sob concorrencia.
    /// Cenario: Multiplos reenvios simultaneos de um mesmo debito ocorrendo em paralelo com consultas.
    /// Justificativa: Evita anomalias de leitura suja ou contagem duplicada durante corridas criticas.
    /// </summary>
    [Fact]
    public async Task Combinacao_DebitoComRetryIdempotenteConcorrenteComConsultaConsolidado()
    {
        // Arranjo: Cria saldo previo de R$ 1000
        var comercianteId = "COMERCIANTE_PAR_004";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 1000m, "Credit", "Saldo inicial"));

        var chaveDebito = Guid.NewGuid().ToString();
        var requisicaoDebito = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 200m,
            Type: "Debit",
            Description: "Debito idempotente concorrente",
            IdempotencyKey: chaveDebito
        );

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao: Dispara 8 tentativas do mesmo debito e 8 consultas de saldo misturadas
        var tarefasDebito = Enumerable.Range(0, 8)
            .Select(_ => _cliente.RegistrarLancamentoBrutoAsync(requisicaoDebito));

        var tarefasConsulta = Enumerable.Range(0, 8)
            .Select(_ => _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje));

        await Task.WhenAll(tarefasDebito.Cast<Task>().Concat(tarefasConsulta.Cast<Task>()));

        // Assercoes finais
        var consolidadoFinal = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidadoFinal!.TotalCredits.Should().Be(1000m);
        consolidadoFinal.TotalDebits.Should().Be(200m, "apenas 1 debito de 200 deve ter sido computado");
        consolidadoFinal.ClosingBalance.Should().Be(800m);
        consolidadoFinal.TransactionCount.Should().Be(2, "1 credito + 1 debito unico");
    }

    /// <summary>
    /// Combinacao: F1 (Credito) + F6 (Consumo & DLQ).
    /// Cenario: Mensagem venenosa inserida na fila de eventos imediatamente antes de transacao valida.
    /// Justificativa: O isolamento da mensagem toxica na DLQ deve permitir que o credito seguinte
    /// seja consolidado perfeitamente sem degradar o sistema.
    /// </summary>
    [Fact]
    public async Task Combinacao_MensagemVenenoIntercaladaEntreCreditos_DeveEncaminharDlqEProcessarSaldoCorreto()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_PAR_005";

        // Acao: Injeta veneno malformado
        _ambiente.InjetarMensagemVenenoNoWorker("{ \"json_malformado\": [}");

        // Em seguida, registra transacao legitima
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 730m,
            Type: "Credit",
            Description: "Credito apos mensagem toxica"
        ));

        // Assercoes
        _ambiente.ObterContagemFilaDlq().Should().Be(1, "a mensagem com erro estrutural deve ser isolada na DLQ");

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidado!.ClosingBalance.Should().Be(730m);
        consolidado.TransactionCount.Should().Be(1);
    }

    /// <summary>
    /// Combinacao: F1 (Credito) + F2 (Debito) + F4 (Cache) com multiplos comerciantes.
    /// Cenario: Multi-tenancy com 5 comerciantes realizando vendas e despesas em paralelo.
    /// Justificativa: Comprova que nenhum saldo ou contador vaza entre comerciantes distintos.
    /// </summary>
    [Fact]
    public async Task Combinacao_MultiplosComerciantesConcorrentesNoMesmoDia_DeveGarantirIsolamentoTotal()
    {
        // Arranjo
        var comerciantes = Enumerable.Range(1, 5).Select(i => $"COMERCIANTE_MULTI_{i}").ToArray();
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Acao: Cada comerciante registra R$ 100 * i em credito e R$ 20 * i em debito
        var tarefas = comerciantes.SelectMany((c, idx) =>
        {
            var i = idx + 1;
            return new[]
            {
                _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(c, 100m * i, "Credit", $"Credito {i}")),
                _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(c, 20m * i, "Debit", $"Debito {i}"))
            };
        }).ToArray();

        await Task.WhenAll(tarefas);

        // Assercoes individuais para cada comerciante
        for (var idx = 0; idx < comerciantes.Length; idx++)
        {
            var i = idx + 1;
            var c = comerciantes[idx];
            var consolidado = await _cliente.ConsultarConsolidadoAsync(c, dataHoje);

            consolidado.Should().NotBeNull();
            consolidado!.TotalCredits.Should().Be(100m * i);
            consolidado.TotalDebits.Should().Be(20m * i);
            consolidado.ClosingBalance.Should().Be(80m * i, "100*i - 20*i = 80*i");
            consolidado.TransactionCount.Should().Be(2);
        }
    }

    /// <summary>
    /// Combinacao: F1 (Credito) + F7 (Restricoes pt-BR & Zero Emojis).
    /// Cenario: Cadastro de lancamentos contendo nomes e termos formais em portugues com acentos.
    /// Justificativa: Assegura que todo o fluxo ate a publicacao de eventos e retorno de consulta
    /// mantenha a fidelidade textual sem perdas ou corrupcao de caracteres especiais.
    /// </summary>
    [Fact]
    public async Task Combinacao_CreditoComCaracteresAcentuadosPtBr_DeveRefletirNoConsolidadoSemCorrupcaoDeEncoding()
    {
        // Arranjo
        var descricaoPtBr = "Liquidação de débito por antecipação de recebíveis — Operação Autorizada";
        var comercianteId = "COMERCIANTE_PAR_007";

        // Acao
        var lancamento = await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 1450.50m,
            Type: "Credit",
            Description: descricaoPtBr
        ));

        // Assercoes
        lancamento.Should().NotBeNull();
        var eventos = _ambiente.ObterEventosPublicados();
        var eventoPublicado = eventos.First(e => e.TransactionId == lancamento!.TransactionId);
        eventoPublicado.Description.Should().Be(descricaoPtBr);

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidado!.ClosingBalance.Should().Be(1450.50m);
    }

    /// <summary>
    /// Combinacao: F4 (Cache Hit) + F5 (Fallback).
    /// Cenario: Alternancia dinamica entre cache saudavel e fallback por falha simulada.
    /// Justificativa: O saldo de fechamento retornado deve ser estritamente identico
    /// independente de ser servido pela camada Redis ou PostgreSQL.
    /// </summary>
    [Fact]
    public async Task Combinacao_AlternanciaDinamicaEntreCacheHitEFallback_DeveManterSaldoIdentico()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_PAR_008";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 600m, "Credit", "Carga"));
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 150m, "Debit", "Retirada"));
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // 1. Leitura inicial via Cache Redis
        var leituraCache1 = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        leituraCache1!.Cached.Should().BeTrue();
        leituraCache1.ClosingBalance.Should().Be(450m);

        // 2. Queda simulada do Redis -> Leitura via Fallback PostgreSQL
        _ambiente.SimularFalhaRedis(true);
        var leituraFallback = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        leituraFallback!.Cached.Should().BeFalse();
        leituraFallback.ClosingBalance.Should().Be(450m, "o saldo deve ser rigorosamente o mesmo");

        // 3. Recuperacao do Redis -> Leitura restabelecida em Cache
        _ambiente.SimularFalhaRedis(false);
        var leituraCache2 = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        leituraCache2!.Cached.Should().BeTrue();
        leituraCache2.ClosingBalance.Should().Be(450m);
    }

    /// <summary>
    /// Combinacao: F1 (Credito) + F2 (Debito) + F3 (Idempotencia).
    /// Cenario: Simulacao completa de estorno financeiro de venda balcao.
    /// Justificativa: Credito de venda seguido de estorno integral (debito de mesmo valor) com idempotencia;
    /// o saldo final deve fechar exatamente em R$ 0,00 com 2 transacoes registradas.
    /// </summary>
    [Fact]
    public async Task Combinacao_SimulacaoDeEstornoCompleto_SaldoFinalDeveRetornarAZero()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_PAR_009";
        var chaveVenda = $"VENDA-{Guid.NewGuid()}";
        var chaveEstorno = $"ESTORNO-{Guid.NewGuid()}";

        // Acao: Venda balcao de R$ 349,90
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 349.90m,
            Type: "Credit",
            Description: "Venda de mercadoria",
            IdempotencyKey: chaveVenda
        ));

        // Estorno integral da venda
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 349.90m,
            Type: "Debit",
            Description: "Estorno de venda cancelada pelo consumidor",
            IdempotencyKey: chaveEstorno
        ));

        // Assercoes no saldo final
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado!.TotalCredits.Should().Be(349.90m);
        consolidado.TotalDebits.Should().Be(349.90m);
        consolidado.ClosingBalance.Should().Be(0.00m, "venda de 349.90 seguida de estorno de 349.90 deve zerar o saldo");
        consolidado.TransactionCount.Should().Be(2);
    }

    /// <summary>
    /// Combinacao: F2 (Debito) + F3 (Idempotencia) + F5 (Fallback).
    /// Cenario: Reenvio de debito idempotente durante falha do Redis e posterior validacao em cache recuperado.
    /// Justificativa: Garante que a gravacao da chave de idempotencia ocorra no banco relacional
    /// e permaneca consistente apos o restabelecimento do cache.
    /// </summary>
    [Fact]
    public async Task Combinacao_IdempotenciaComDebitoAposRecuperacaoDeFalhaDoRedis()
    {
        // Arranjo: Redis em falha
        _ambiente.SimularFalhaRedis(true);
        var comercianteId = "COMERCIANTE_PAR_010";
        var chave = Guid.NewGuid().ToString();

        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 500m,
            Type: "Debit",
            Description: "Debito sob falha com idempotencia",
            IdempotencyKey: chave
        );

        // Acao: Envia duas vezes sob falha
        var resp1 = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);
        var resp2 = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);
        resp1.StatusCode.Should().Be(HttpStatusCode.Created);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Restabelece Redis
        _ambiente.SimularFalhaRedis(false);

        // Assercoes
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado!.TotalDebits.Should().Be(500m);
        consolidado.TransactionCount.Should().Be(1);
    }

    /// <summary>
    /// Combinacao: F1 (Credito) + F7 (Zero Emojis) + F4 (Consolidado).
    /// Cenario: Tentativa de insercao invalida contendo emojis contra um comerciante com saldo existente.
    /// Justificativa: Requisicoes rejeitadas com HTTP 400 por violacao de emojis nao podem
    /// poluir, corromper ou mutacionar o saldo previamente consolidado.
    /// </summary>
    [Fact]
    public async Task Combinacao_RejeicaoDeEmojiEmPayloadDeCreditoNaoDeveAfetarConsolidadoExistente()
    {
        // Arranjo: Credito previo legitimo
        var comercianteId = "COMERCIANTE_PAR_011";
        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(comercianteId, 400m, "Credit", "Credito inicial"));

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidadoAntes = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidadoAntes!.ClosingBalance.Should().Be(400m);

        // Acao: Requisicao com emoji proibido
        var emoji = char.ConvertFromUtf32(0x1F911); // Carinha com dinheiro
        var requisicaoComEmoji = new RequisicaoLancamento(comercianteId, 100m, "Credit", $"Venda balcao {emoji}");
        var respostaInvalida = await _cliente.RegistrarLancamentoBrutoAsync(requisicaoComEmoji);

        // Assercoes
        respostaInvalida.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var consolidadoDepois = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidadoDepois!.ClosingBalance.Should().Be(400m, "o saldo nao deve sofrer mutacao por requisicao rejeitada");
        consolidadoDepois.TransactionCount.Should().Be(1);
    }

    /// <summary>
    /// Combinacao: F1 (Credito) + F2 (Debito) + F6 (Consumo Idempotente).
    /// Cenario: Ingestao assincrona duplicada de evento de credito e de debito no worker.
    /// Justificativa: Garante que tanto eventos de credito quanto de debito sejam deduplicados
    /// individualmente pelo identificador de evento (EventId), mantendo a precisao do saldo.
    /// </summary>
    [Fact]
    public async Task Combinacao_DuploProcessamentoDeEventoWorkerComCreditoEDebito_DeveDeduplicarAmbos()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_PAR_012";
        var eventoCreditoId = Guid.NewGuid();
        var eventoDebitoId = Guid.NewGuid();
        var dataAtual = DateTime.UtcNow;

        var eventoCredito = new EventoLancamentoCriado(
            eventoCreditoId, Guid.NewGuid(), comercianteId, 300m, "Credit", "Credito assincrono", dataAtual, dataAtual
        );

        var eventoDebito = new EventoLancamentoCriado(
            eventoDebitoId, Guid.NewGuid(), comercianteId, 100m, "Debit", "Debito assincrono", dataAtual, dataAtual
        );

        // Acao: Injeta cada evento duas vezes no worker
        _ambiente.InjetarMensagemVenenoNoWorker(System.Text.Json.JsonSerializer.Serialize(eventoCredito));
        _ambiente.InjetarMensagemVenenoNoWorker(System.Text.Json.JsonSerializer.Serialize(eventoCredito)); // Duplicata

        _ambiente.InjetarMensagemVenenoNoWorker(System.Text.Json.JsonSerializer.Serialize(eventoDebito));
        _ambiente.InjetarMensagemVenenoNoWorker(System.Text.Json.JsonSerializer.Serialize(eventoDebito)); // Duplicata

        // Assercoes
        var dataHoje = DateOnly.FromDateTime(dataAtual).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado!.TotalCredits.Should().Be(300m);
        consolidado.TotalDebits.Should().Be(100m);
        consolidado.ClosingBalance.Should().Be(200m);
        consolidado.TransactionCount.Should().Be(2, "apenas 1 credito e 1 debito unicos devem ser contabilizados");
    }
}
