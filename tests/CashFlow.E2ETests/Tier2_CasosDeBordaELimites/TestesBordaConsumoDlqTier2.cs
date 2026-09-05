using System.Text.Json;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier2_CasosDeBordaELimites;

/// <summary>
/// Suite de testes E2E do Tier 2 para Casos de Borda e Limites em Consumo Idempotente e DLQ.
/// Valida condicoes anormais de mensageria: tipos nao reconhecidos, valores numericos invalidos,
/// rajadas de poison messages e resiliencia continuada do pipeline de consumo do RabbitMQ.
/// </summary>
public class TestesBordaConsumoDlqTier2 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesBordaConsumoDlqTier2()
    {
        _ambiente = new AmbienteTesteE2E();
        _cliente = new ClienteApiFluxoCaixa(_ambiente.CriarClienteHttp());
    }

    public void Dispose()
    {
        _ambiente.LimparEstado();
    }

    /// <summary>
    /// Cenario: Ingestao de payload completamente vazio ou espacos em branco na fila de eventos.
    /// Justificativa: Nao deve quebrar o BackgroundService do worker;
    /// deve encaminhar diretamente para a fila de cartas mortas (DLQ).
    /// </summary>
    [Fact]
    public void DeveEncaminharPayloadVazioOuEmBrancoDiretamenteParaDlq()
    {
        // Arranjo
        var payloadVazio = "   ";

        // Acao
        _ambiente.InjetarMensagemVenenoNoWorker(payloadVazio);

        // Assercoes
        _ambiente.ObterContagemFilaDlq().Should().Be(1);
        _ambiente.ObterMensagensFilaDlq().First().Should().Be(payloadVazio);
    }

    /// <summary>
    /// Cenario: Evento contendo tipo de transacao nao reconhecido (ex.: "Transfer", "Refund", "Estorno").
    /// Justificativa: Apenas 'Credit' e 'Debit' sao tipos validos para mutacao no dominio;
    /// outros tipos recebidos em mensagens corrompidas devem ir para a DLQ.
    /// </summary>
    [Fact]
    public void DeveEncaminharEventoComTipoDeTransacaoDesconhecidoParaDlq()
    {
        // Arranjo
        var eventoTipoInvalido = new
        {
            eventId = Guid.NewGuid(),
            transactionId = Guid.NewGuid(),
            merchantId = "COMERCIANTE_BORDA_DLQ_002",
            amount = 150m,
            type = "TipoInexistente",
            createdAt = DateTime.UtcNow
        };
        var payload = JsonSerializer.Serialize(eventoTipoInvalido);

        // Acao
        _ambiente.InjetarMensagemVenenoNoWorker(payload);

        // Assercoes
        _ambiente.ObterContagemFilaDlq().Should().Be(1);
    }

    /// <summary>
    /// Cenario: Evento com valor financeiro menor ou igual a zero chegado na fila de mensageria.
    /// Justificativa: Mesmo que uma mensagem invalida ultrapasse o publicador, o consumidor
    /// deve ter validacao defensiva e rejeitar para a DLQ sem distorcer o saldo.
    /// </summary>
    [Fact]
    public void DeveEncaminharEventoComValorMonetarioInvalidoParaDlq()
    {
        // Arranjo: Evento com valor -50
        var eventoValorInvalido = new
        {
            eventId = Guid.NewGuid(),
            transactionId = Guid.NewGuid(),
            merchantId = "COMERCIANTE_BORDA_DLQ_003",
            amount = -50m,
            type = "Credit",
            createdAt = DateTime.UtcNow
        };
        var payload = JsonSerializer.Serialize(eventoValorInvalido);

        // Acao
        _ambiente.InjetarMensagemVenenoNoWorker(payload);

        // Assercoes
        _ambiente.ObterContagemFilaDlq().Should().Be(1);
    }

    /// <summary>
    /// Cenario: Rajada massiva de 50 mensagens venenosas seguidas.
    /// Justificativa: Comprova que uma rajada de mensagens corrompidas nao congestiona o consumidor
    /// nem derruba o pod/processo do worker.
    /// </summary>
    [Fact]
    public void DeveSuportarSobrecargaDeMultiplasMensagensVenenoIsolandoTodasNaDlq()
    {
        // Arranjo
        var totalVenenos = 50;

        // Acao
        for (var i = 1; i <= totalVenenos; i++)
        {
            _ambiente.InjetarMensagemVenenoNoWorker($"VENENO_MASSIVO_{i}: {{ corrupt: true }}");
        }

        // Assercoes
        _ambiente.ObterContagemFilaDlq().Should().Be(totalVenenos, "todas as 50 mensagens invalidas devem estar na DLQ");
    }

    /// <summary>
    /// Cenario: Processamento de mensagem valida intercalada entre mensagens com veneno.
    /// Justificativa: Garante que as mensagens validas tenham seu saldo consolidado com exatidao
    /// e que a presenca de falhas intermediarias nao afete os eventos legitimos.
    /// </summary>
    [Fact]
    public async Task DeveProcessarCorretamenteMensagemValidaIntercaladaEntreMensagensVeneno()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_BORDA_DLQ_005";

        // Acao: Injeta veneno 1, registra evento valido via API, injeta veneno 2
        _ambiente.InjetarMensagemVenenoNoWorker("VENENO_ANTERIOR");

        await _cliente.RegistrarLancamentoAsync(new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 880.00m,
            Type: "Credit",
            Description: "Transacao valida central"
        ));

        _ambiente.InjetarMensagemVenenoNoWorker("VENENO_POSTERIOR");

        // Assercoes
        _ambiente.ObterContagemFilaDlq().Should().Be(2);

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);

        consolidado!.ClosingBalance.Should().Be(880.00m);
        consolidado.TransactionCount.Should().Be(1);
    }
}
