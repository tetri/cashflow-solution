using System.Net;
using CashFlow.E2ETests.Infraestrutura;
using FluentAssertions;
using Xunit;

namespace CashFlow.E2ETests.Tier2_CasosDeBordaELimites;

/// <summary>
/// Suite de testes E2E do Tier 2 para Casos de Borda e Limites em Idempotencia de Escrita.
/// Avalia comportamentos sob chaves nulas, formatos atipicos (sem hifens, tamanhos extensos)
/// e tentativas de violacao de integridade com cargas divergentes.
/// </summary>
public class TestesBordaIdempotenciaTier2 : IDisposable
{
    private readonly AmbienteTesteE2E _ambiente;
    private readonly ClienteApiFluxoCaixa _cliente;

    public TestesBordaIdempotenciaTier2()
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
    /// Cenario: Requisicao sem chave de idempotencia informada (nula ou vazia).
    /// Justificativa: A chave e opcional pelo contrato; transacoes sem chave devem ser
    /// tratadas como operacoes comuns e criar lancamentos distintos sucessivos.
    /// </summary>
    [Fact]
    public async Task DeveAceitarLancamentoComChaveDeIdempotenciaNulaOuVaziaTratandoComoNaoIdempotente()
    {
        // Arranjo
        var comercianteId = "COMERCIANTE_BORDA_IDEMP_001";
        var requisicao = new RequisicaoLancamento(
            MerchantId: comercianteId,
            Amount: 110.00m,
            Type: "Credit",
            Description: "Lancamento sem chave de idempotencia",
            IdempotencyKey: null
        );

        // Acao: Registra duas vezes
        var lancamento1 = await _cliente.RegistrarLancamentoAsync(requisicao);
        var lancamento2 = await _cliente.RegistrarLancamentoAsync(requisicao);

        // Assercoes
        lancamento1!.TransactionId.Should().NotBe(lancamento2!.TransactionId, "transacoes sem chave de idempotencia geram IDs unicos distintos");

        var dataHoje = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var consolidado = await _cliente.ConsultarConsolidadoAsync(comercianteId, dataHoje);
        consolidado!.TransactionCount.Should().Be(2);
        consolidado.TotalCredits.Should().Be(220.00m);
    }

    /// <summary>
    /// Cenario: Chave de idempotencia em formato UUID hexadecimal contínuo (sem hifens).
    /// Justificativa: Compatibilidade com sistemas legado que enviam UUIDs normalizados como "N".
    /// </summary>
    [Fact]
    public async Task DeveSuportarChaveDeIdempotenciaUuidSemHifens()
    {
        // Arranjo: Formato "N" (ex.: 32 caracteres hexadecimais sem separador)
        var chaveSemHifens = Guid.NewGuid().ToString("N");
        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_IDEMP_002",
            Amount: 310.00m,
            Type: "Credit",
            Description: "UUID continuo",
            IdempotencyKey: chaveSemHifens
        );

        // Acao
        var resposta1 = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);
        var resposta2 = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        resposta1.StatusCode.Should().Be(HttpStatusCode.Created);
        resposta2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Cenario: Chave de idempotencia com tamanho estendido de ate 128 caracteres.
    /// Justificativa: Permite chaves compostas de integracao (ex.: sistema-filial-timestamp-hash).
    /// </summary>
    [Fact]
    public async Task DeveSuportarChaveDeIdempotenciaLongaComAteCentoEVinteEOitoCaracteres()
    {
        // Arranjo: String de 128 caracteres
        var chaveLonga = $"CHAVE-COMPOSTA-INTEGRACAO-{new string('K', 90)}-FIM";
        chaveLonga.Length.Should().BeLessThanOrEqualTo(128);

        var requisicao = new RequisicaoLancamento(
            MerchantId: "COMERCIANTE_BORDA_IDEMP_003",
            Amount: 50.00m,
            Type: "Credit",
            Description: "Chave longa",
            IdempotencyKey: chaveLonga
        );

        // Acao
        var resposta1 = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);
        var resposta2 = await _cliente.RegistrarLancamentoBrutoAsync(requisicao);

        // Assercoes
        resposta1.StatusCode.Should().Be(HttpStatusCode.Created);
        resposta2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Cenario: Reutilizacao de chave de idempotencia por comerciante diferente.
    /// Justificativa: Uma chave de idempotencia deve ser vinculada ao contexto original;
    /// se outro comerciante tentar reutilizar a mesma chave, gera conflito 409.
    /// </summary>
    [Fact]
    public async Task DeveRetornarConflito409QuandoMesmaChaveForUsadaComComercianteDiferente()
    {
        // Arranjo
        var chave = Guid.NewGuid().ToString();
        var requisicaoA = new RequisicaoLancamento("COMERCIANTE_A", 100m, "Credit", "Venda A", chave);
        var requisicaoB = new RequisicaoLancamento("COMERCIANTE_B", 100m, "Credit", "Venda B", chave);

        // Acao
        await _cliente.RegistrarLancamentoBrutoAsync(requisicaoA);
        var respostaConflito = await _cliente.RegistrarLancamentoBrutoAsync(requisicaoB);

        // Assercoes
        respostaConflito.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Cenario: Reutilizacao da mesma chave invertendo o tipo de lancamento (Credit -> Debit).
    /// Justificativa: Garantir que a alteracao semantica do tipo nao passe despercebida pela idempotencia.
    /// </summary>
    [Fact]
    public async Task DeveRetornarConflito409QuandoMesmaChaveForUsadaComTipoDeTransacaoInvertido()
    {
        // Arranjo
        var chave = Guid.NewGuid().ToString();
        var requisicaoOriginal = new RequisicaoLancamento("COMERCIANTE_BORDA_IDEMP_005", 250m, "Credit", "Credito", chave);
        var requisicaoInvertida = new RequisicaoLancamento("COMERCIANTE_BORDA_IDEMP_005", 250m, "Debit", "Debito", chave);

        // Acao
        await _cliente.RegistrarLancamentoBrutoAsync(requisicaoOriginal);
        var respostaConflito = await _cliente.RegistrarLancamentoBrutoAsync(requisicaoInvertida);

        // Assercoes
        respostaConflito.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
