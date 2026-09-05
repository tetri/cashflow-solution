using CashFlow.Transactions.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace CashFlow.Transactions.UnitTests.Domain;

/// <summary>
/// Suite de testes unitarios para a entidade raiz de agregacao Transaction.
/// Valida todas as invariantes de negocio do DDD: limites de tamanho de campos,
/// precisao de valores monetarios, tipos contabeis permitidos, idempotencia e caracteres pt-BR.
/// </summary>
public class TransactionTests
{
    /// <summary>
    /// Valida que uma transacao de credito e instanciada com sucesso gerando UUID, data UTC e campos validos.
    /// </summary>
    [Fact]
    public void Construtor_ComParametrosValidosDeCredito_DeveInstanciarComSucesso()
    {
        // Arrange & Act
        var transacao = new Transaction("MERCH_01", 250.75m, TransactionType.Credit, "Recebimento de vendas", "chave-idemp-01");

        // Assert
        transacao.Id.Should().NotBeEmpty();
        transacao.MerchantId.Should().Be("MERCH_01");
        transacao.Amount.Should().Be(250.75m);
        transacao.Type.Should().Be(TransactionType.Credit);
        transacao.Description.Should().Be("Recebimento de vendas");
        transacao.IdempotencyKey.Should().Be("chave-idemp-01");
        transacao.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Valida que o metodo de fabrica expressivo CreateCredit cria adequadamente um lancamento do tipo Credit.
    /// </summary>
    [Fact]
    public void CreateCredit_ComParametrosValidos_DeveRetornarTransacaoTipoCredit()
    {
        // Act
        var transacao = Transaction.CreateCredit("MERCH_02", 100.00m, "Venda balcao", "chave-02");

        // Assert
        transacao.Type.Should().Be(TransactionType.Credit);
        transacao.Amount.Should().Be(100.00m);
        transacao.MerchantId.Should().Be("MERCH_02");
        transacao.Description.Should().Be("Venda balcao");
        transacao.IdempotencyKey.Should().Be("chave-02");
    }

    /// <summary>
    /// Valida que o metodo de fabrica expressivo CreateDebit cria adequadamente um lancamento do tipo Debit.
    /// </summary>
    [Fact]
    public void CreateDebit_ComParametrosValidos_DeveRetornarTransacaoTipoDebit()
    {
        // Act
        var transacao = Transaction.CreateDebit("MERCH_03", 50.25m, "Pagamento fornecedor", "chave-03");

        // Assert
        transacao.Type.Should().Be(TransactionType.Debit);
        transacao.Amount.Should().Be(50.25m);
        transacao.MerchantId.Should().Be("MERCH_03");
        transacao.Description.Should().Be("Pagamento fornecedor");
        transacao.IdempotencyKey.Should().Be("chave-03");
    }

    /// <summary>
    /// Valida que valores monetarios menores ou iguais a zero disparam ArgumentException com mensagem didatica.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-500)]
    public void Construtor_ComValorMenorOuIgualAZero_DeveLancarArgumentException(decimal valorInvalido)
    {
        // Act
        var act = () => new Transaction("MERCH_01", valorInvalido, TransactionType.Credit, "Descricao valida");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*maior que zero*");
    }

    /// <summary>
    /// Valida que MerchantId nulo, vazio ou contendo apenas espacos e sumariamente rejeitado pelo dominio.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Construtor_ComMerchantIdInvalido_DeveLancarArgumentException(string? merchantIdInvalido)
    {
        // Act
        var act = () => new Transaction(merchantIdInvalido!, 100m, TransactionType.Credit, "Descricao valida");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MerchantId*obrigatorio*");
    }

    /// <summary>
    /// Valida que MerchantId com tamanho superior a 50 caracteres dispara ArgumentException.
    /// </summary>
    [Fact]
    public void Construtor_ComMerchantIdExcedendo50Caracteres_DeveLancarArgumentException()
    {
        // Arrange
        var merchantIdLongo = new string('M', 51);

        // Act
        var act = () => new Transaction(merchantIdLongo, 100m, TransactionType.Credit, "Descricao valida");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*nao pode exceder 50 caracteres*");
    }

    /// <summary>
    /// Valida que MerchantId com exatamente 50 caracteres e aceito no limite de borda.
    /// </summary>
    [Fact]
    public void Construtor_ComMerchantIdNoLimiteDe50Caracteres_DeveCriarComSucesso()
    {
        // Arrange
        var merchantIdExato = new string('M', 50);

        // Act
        var transacao = new Transaction(merchantIdExato, 100m, TransactionType.Credit, "Descricao valida");

        // Assert
        transacao.MerchantId.Should().Be(merchantIdExato);
    }

    /// <summary>
    /// Valida que descricao nula, vazia ou contendo apenas espacos e sumariamente rejeitada.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Construtor_ComDescricaoInvalida_DeveLancarArgumentException(string? descricaoInvalida)
    {
        // Act
        var act = () => new Transaction("MERCH_01", 100m, TransactionType.Credit, descricaoInvalida!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*descricao*obrigatoria*");
    }

    /// <summary>
    /// Valida que descricoes comerciais com mais de 500 caracteres sao rejeitadas pelo dominio.
    /// </summary>
    [Fact]
    public void Construtor_ComDescricaoExcedendo500Caracteres_DeveLancarArgumentException()
    {
        // Arrange
        var descricaoLonga = new string('D', 501);

        // Act
        var act = () => new Transaction("MERCH_01", 100m, TransactionType.Credit, descricaoLonga);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*nao pode exceder 500 caracteres*");
    }

    /// <summary>
    /// Valida que tipo contabil fora do enum TransactionType lanca ArgumentException.
    /// </summary>
    [Fact]
    public void Construtor_ComTipoContabilInvalido_DeveLancarArgumentException()
    {
        // Arrange
        var tipoInvalido = (TransactionType)99;

        // Act
        var act = () => new Transaction("MERCH_01", 100m, tipoInvalido, "Descricao valida");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*tipo de transacao*invalido*");
    }

    /// <summary>
    /// Valida que a chave de idempotencia tem seus espacos em branco aparados (Trim).
    /// </summary>
    [Fact]
    public void Construtor_ComChaveDeIdempotenciaComEspacos_DeveNormalizarChave()
    {
        // Act
        var transacao = new Transaction("MERCH_01", 100m, TransactionType.Credit, "Venda", "  chave-com-espacos  ");

        // Assert
        transacao.IdempotencyKey.Should().Be("chave-com-espacos");
    }

    /// <summary>
    /// Valida que chave de idempotencia contendo apenas espacos ou vazia e normalizada para nulo.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construtor_ComChaveDeIdempotenciaVazia_DeveNormalizarParaNulo(string? chaveVazia)
    {
        // Act
        var transacao = new Transaction("MERCH_01", 100m, TransactionType.Credit, "Venda", chaveVazia);

        // Assert
        transacao.IdempotencyKey.Should().BeNull();
    }

    /// <summary>
    /// Valida que chave de idempotencia excedendo 128 caracteres dispara ArgumentException.
    /// </summary>
    [Fact]
    public void Construtor_ComChaveIdempotenciaExcedendo128Caracteres_DeveLancarArgumentException()
    {
        // Arrange
        var chaveLonga = new string('K', 129);

        // Act
        var act = () => new Transaction("MERCH_01", 100m, TransactionType.Credit, "Venda", chaveLonga);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*nao pode exceder 128 caracteres*");
    }

    /// <summary>
    /// Valida que caracteres da lingua portuguesa (acentos agudos, til, circunflexo e cedilha) sao preservados integralmente.
    /// </summary>
    [Fact]
    public void Construtor_ComCaracteresAcentuadosPtBr_DevePreservarCaracteresLegitimos()
    {
        // Arrange
        var descricaoPtBr = "Transferencia referente a aquisicao de servicos de conciliacao e auditoria contabil";

        // Act
        var transacao = new Transaction("COMERCIANTE_ÁÇÕ", 320.45m, TransactionType.Credit, descricaoPtBr);

        // Assert
        transacao.MerchantId.Should().Be("COMERCIANTE_ÁÇÕ");
        transacao.Description.Should().Be(descricaoPtBr);
    }

    /// <summary>
    /// Valida a precisao do tipo decimal para valores financeiros extremos sem risco de perda de centavos.
    /// </summary>
    [Fact]
    public void Construtor_ComValorExtremo_DevePreservarPrecisaoMonetaria()
    {
        // Arrange
        var valorExtremo = 999999999999.99m;

        // Act
        var transacao = new Transaction("MERCH_01", valorExtremo, TransactionType.Credit, "Liquidacao de ativos");

        // Assert
        transacao.Amount.Should().Be(valorExtremo);
    }
}
