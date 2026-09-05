using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier1_CoberturaFuncionalidades;

/// <summary>
/// Suite de testes E2E do Tier 1 para a funcionalidade de Idempotencia de Escrita.
/// Garante que retentativas de rede, timeouts ou reenvios de clientes HTTP
/// nao causem duplicacao de lancamentos, divergencia de saldos ou duplicidade de eventos.
/// </summary>
public class TestesIdempotenciaEscritaTier1 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesIdempotenciaEscritaTier1()
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
    /// Cenario: Envio de requisicao com chave de idempotencia definida no corpo JSON.
    /// Justificativa: Ao reenviar o mesmo payload com a mesma chave, a API deve retornar
    /// os dados do lancamento original com o mesmo TransactionId.
    /// </summary>
    [Fact]
    public async Task DeveReconhecerChaveDeIdempotenciaNoCorpoERetornarMesmoIdentificador()
    {
        // Arranjo
        var chaveIdempotencia = Guid.NewGuid().ToString();
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_IDEMP_001",
            Amount: 320.00m,
            Type: "Credit",
            Description: "Venda com chave no corpo",
            IdempotencyKey: chaveIdempotencia
        );

        // Acao: Primeiro envio (criacao inicial)
        var respostaPrimeira = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);
        respostaPrimeira.StatusCode.Should().Be(HttpStatusCode.Created);
        var dadosPrimeiro = await _cliente.RegistrarLancamentoAsync(requisicao);

        // Acao: Segundo envio (reenvio idempotente)
        var respostaSegunda = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        respostaSegunda.StatusCode.Should().Be(HttpStatusCode.OK, "reenvios idempotentes confirmados retornam 200 OK");
        var dadosSegundo = await _cliente.RegistrarLancamentoAsync(requisicao);

        dadosSegundo.Should().NotBeNull();
        dadosSegundo!.TransactionId.Should().Be(dadosPrimeiro!.TransactionId, "o identificador da transacao deve ser exatamente o mesmo");
        dadosSegundo.CreatedAt.Should().Be(dadosPrimeiro.CreatedAt);
    }

    /// <summary>
    /// Cenario: Envio de chave de idempotencia via cabecalho HTTP X-Idempotency-Key.
    /// Justificativa: Permite flexibilidade aos clientes que adotam cabecalhos padronizados HTTP.
    /// </summary>
    [Fact]
    public async Task DeveReconhecerChaveDeIdempotenciaNoCabecalhoHttpERetornarStatus200()
    {
        // Arranjo
        var chaveCabecalho = $"CHAVE-CABECALHO-{Guid.NewGuid()}";
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_IDEMP_002",
            Amount: 145.90m,
            Type: "Debit",
            Description: "Pagamento com chave no cabecalho"
        );

        // Acao
        var resposta1 = await _cliente.RegistrarLancamentoBrutoAsync(requisicao, chaveCabecalho);
        resposta1.StatusCode.Should().Be(HttpStatusCode.Created);

        var resposta2 = await _cliente.RegistrarLancamentoBrutoAsync(requisicao, chaveCabecalho);
        resposta2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Cenario: Verificacao de que o saldo consolidado e os eventos nao sao duplicados.
    /// Justificativa: O principio central da idempotencia em sistemas financeiros e evitar
    /// credito ou debito duplicado na contabilidade e na fila de eventos.
    /// </summary>
    [Fact]
    public async Task NaoDeveDuplicarEventoNemSaldoAoReenviarMesmoLancamentoIdempotente()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_IDEMP_003";
        var chave = Guid.NewGuid().ToString();
        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 500.00m,
            Type: "Credit",
            Description: "Credito idempotente unico",
            IdempotencyKey: chave
        );

        // Acao: Envia 5 vezes consecutivas a mesma requisicao com a mesma chave
        for (var i = 0; i < 5; i++)
        {
            await _cliente.RegistrarLancamentoBrutoAsync(requisicao);
        }

        // Assercoes no barramento de eventos: deve conter estritamente 1 evento gerado
        var eventos = _ambiente.ObterEventosPublicados();
        eventos.Count(e => e.MerchantId == comercianteId).Should().Be(1, "apenas 1 evento deve ser publicado para lancamentos idempotentes repetidos");

        // Assercoes no saldo consolidado: deve refletir apenas uma adicao de R$ 500,00
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.TotalCredits.Should().Be(500.00m);
        consolidado.ClosingBalance.Should().Be(500.00m);
        consolidado.TransactionCount.Should().Be(1, "a contagem de transacoes do dia deve ser estritamente 1");
    }

    /// <summary>
    /// Cenario: Reutilizacao de chave de idempotencia com carga util divergente.
    /// Justificativa: Previne ataques ou erros operacionais onde uma mesma chave e reciclada
    /// para tentar autorizar uma transacao diferente (conflito 409 Conflict).
    /// </summary>
    [Fact]
    public async Task DeveRetornarConflito409QuandoMesmaChaveForUsadaComDadosDivergentes()
    {
        // Arranjo
        var chaveCompartilhada = Guid.NewGuid().ToString();
        var requisicaoOriginal = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_IDEMP_004",
            Amount: 200.00m,
            Type: "Credit",
            Description: "Transacao legitima original",
            IdempotencyKey: chaveCompartilhada
        );

        var requisicaoDivergente = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_IDEMP_004",
            Amount: 999.00m, // Valor divergente
            Type: "Credit",
            Description: "Tentativa fraudulenta com mesmo token",
            IdempotencyKey: chaveCompartilhada
        );

        // Acao
        var respostaOriginal = await _cliente.RegistrarLancamentoBrutoAsync(requisicaoOriginal);
        respostaOriginal.StatusCode.Should().Be(HttpStatusCode.Created);

        var respostaConflito = await _cliente.RegistrarLancamentoBrutoAsync(requisicaoDivergente);

        // Assercoes
        respostaConflito.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var detalhes = await ClienteApiFluxoCaixa.ExtrairDetalhesProblemaAsync(respostaConflito);
        detalhes.Should().NotBeNull();
        detalhes!.Status.Should().Be(409);
        detalhes.Detail.Should().Contain("chave de idempotencia", "a mensagem deve esclarecer o conflito da chave");
    }

    /// <summary>
    /// Cenario: Envio concorrente simultaneo de multiplas requisicoes com a mesma chave de idempotencia.
    /// Justificativa: Testa condicoes de corrida (race conditions) no banco de dados e na memoria,
    /// garantindo atomicidade na insercao.
    /// </summary>
    [Fact]
    public async Task DeveManterIdempotenciaEmRequisicoesConcorrentesComMesmaChave()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_IDEMP_005";
        var chave = Guid.NewGuid().ToString();
        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 150.00m,
            Type: "Debit",
            Description: "Lancamento sob concorrencia",
            IdempotencyKey: chave
        );

        // Acao: Dispara 10 tarefas HTTP em paralelo absoluto
        var tarefas = Enumerable.Range(0, 10)
            .Select(_ => _cliente.RegistrarLancamentoBrutoAsync(requisicao))
            .ToArray();

        var respostas = await Task.WhenAll(tarefas);

        // Assercoes
        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "exatamente uma requisicao deve criar o recurso (201)");
        respostas.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(9, "todas as outras 9 requisicoes paralelas devem receber 200 OK");

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidado!.TransactionCount.Should().Be(1);
        consolidado.TotalDebits.Should().Be(150.00m);
    }
}
