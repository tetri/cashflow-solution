using System.Text.Json;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier1_CoberturaFuncionalidades;

/// <summary>
/// Suite de testes E2E do Tier 1 para Consumo Idempotente e Dead Letter Queue (DLQ).
/// Valida a resiliencia da camada assincrona de mensageria, deduplicacao de eventos
/// pelo worker contra a tabela processed_events e isolamento de mensagens venenosas (poison messages).
/// </summary>
public class TestesConsumoIdempotenteDlqTier1 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesConsumoIdempotenteDlqTier1()
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
    /// Cenario: Consumo de evento valido com atualizacao no PostgreSQL e registro de evento processado.
    /// Justificativa: Comprova que um evento publicado pelo servico de lancamentos e consumido
    /// com sucesso pelo worker, persistindo o consolidado.
    /// </summary>
    [Fact]
    public async Task DeveConsumirEventoComSucessoEAtualizarBaseRelacionalEIdempotencia()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_CONSUMO_001";
        var requisicao = new RequisicaoLancamento(comercianteId, 350.00m, "Credit", "Venda de produto");

        // Acao: Registra via API (o ambiente despacha o evento para o worker)
        var lancamento = await _cliente.RegistrarLancamentoAsync(requisicao);

        // Assercoes
        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.ClosingBalance.Should().Be(350.00m);
        consolidado.TransactionCount.Should().Be(1);

        var eventos = _ambiente.ObterEventosPublicados();
        eventos.Should().Contain(e => e.TransactionId == lancamento!.TransactionId);
    }

    /// <summary>
    /// Cenario: Reenvio do mesmo evento com o mesmo EventId.
    /// Justificativa: Falhas no RabbitMQ ou reinicios do worker podem causar redelivery;
    /// o worker deve checar processed_events e descartar a duplicata com Ack sem recalcular saldo.
    /// </summary>
    [Fact]
    public async Task NaoDeveProcessarDuasVezesEventoComMesmoIdentificadorDeEvento()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_CONSUMO_002";
        var eventoId = Guid.NewGuid();
        var dataAtual = DateTime.UtcNow;

        var evento = new EventoLancamentoCriado(
            EventId: eventoId,
            TransactionId: Guid.NewGuid(),
            MerchantId: comercianteId,
            Amount: 200.00m,
            Type: "Credit",
            Description: "Entrada em duplicidade de mensageria",
            CreatedAt: dataAtual,
            OccurredOn: dataAtual
        );

        var payloadJson = JsonSerializer.Serialize(evento);

        // Acao: Injeta a mensagem duas vezes seguidas
        _ambiente.InjetarMensagemVenenoNoWorker(payloadJson);
        _ambiente.InjetarMensagemVenenoNoWorker(payloadJson); // Reentrega duplicada com mesmo EventId

        // Assercoes
        var dataHoje = DateOnly.FromDateTime(dataAtual).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.TotalCredits.Should().Be(200.00m, "o saldo deve ser computado apenas uma unica vez");
        consolidado.TransactionCount.Should().Be(1);
    }

    /// <summary>
    /// Cenario: Ingestao de mensagem com JSON corrompido / malformado.
    /// Justificativa: Mensagens corrompidas nao podem travar a fila principal;
    /// devem ser rejeitadas com BasicNack (requeue=false) e encaminhadas para a DLQ.
    /// </summary>
    [Fact]
    public async Task DeveEncaminharMensagemMalfatadaParaFilaDeCartasMortasDLQ()
    {
        // Arranjo
        var mensagemCorrompida = "{ payload_invalido_sem_fechamento_json: true, ";

        // Acao
        _ambiente.InjetarMensagemVenenoNoWorker(mensagemCorrompida);

        // Assercoes
        _ambiente.ObterContagemFilaDlq().Should().Be(1, "mensagens invalidas devem ser roteadas para a fila DLQ");
        var mensagensDlq = _ambiente.ObterMensagensFilaDlq();
        mensagensDlq[0].Should().Be(mensagemCorrompida);
    }

    /// <summary>
    /// Cenario: Ingestao de mensagem sem campos obrigatorios de negocio (ex.: sem MerchantId).
    /// Justificativa: Eventos semanticamente invalidos devem ser descartados da fila principal
    /// e isolados na DLQ para posterior analise operacional sem derrubar o processo de background.
    /// </summary>
    [Fact]
    public async Task DeveEncaminharMensagemSemComercianteParaDLQSemDerrubarWorker()
    {
        // Arranjo: Evento com MerchantId vazio
        var eventoSemComerciante = new
        {
            eventId = Guid.NewGuid(),
            transactionId = Guid.NewGuid(),
            merchantId = "", // Invalido
            amount = 100m,
            type = "Credit",
            createdAt = DateTime.UtcNow
        };
        var payload = JsonSerializer.Serialize(eventoSemComerciante);

        // Acao
        _ambiente.InjetarMensagemVenenoNoWorker(payload);

        // Assercoes
        _ambiente.ObterContagemFilaDlq().Should().Be(1);
    }

    /// <summary>
    /// Cenario: Continuidade operacional da fila principal apos o processamento de mensagens com veneno.
    /// Justificativa: Garante que um erro em uma mensagem especifica nao bloqueie as mensagens subsequentes
    /// da fila principal (garantia de vazao e eliminacao de efeito domino).
    /// </summary>
    [Fact]
    public async Task DeveManterFilaPrincipalOperacionalMesmoAposReceberMensagensVeneno()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_CONSUMO_005";

        // Passo 1: Injeta 2 mensagens venenosas
        _ambiente.InjetarMensagemVenenoNoWorker("VENENO_1");
        _ambiente.InjetarMensagemVenenoNoWorker("VENENO_2");

        // Passo 2: Envia transacao legitima pelo pipeline oficial
        var resposta = await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 750.00m,
            Type: "Credit",
            Description: "Transacao valida apos veneno"
        ));

        // Assercoes
        resposta.Should().NotBeNull();
        _ambiente.ObterContagemFilaDlq().Should().Be(2, "duas mensagens invalidas devem estar isoladas na DLQ");

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado.Should().NotBeNull();
        consolidado!.ClosingBalance.Should().Be(750.00m, "a transacao valida deve ser processada normalmente");
        consolidado.TransactionCount.Should().Be(1);
    }
}
