using CashFlow.Consolidated.Application.Queries.GetDailyConsolidated;
using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace CashFlow.Consolidated.UnitTests.Application;

/// <summary>
/// Suite de testes unitarios para o manipulador de consultas de consolidado diario (GetDailyConsolidatedQueryHandler).
/// Valida a politica de Cache-Aside: hit prioritario no Redis sem consulta ao banco e miss com recuperacao no PostgreSQL.
/// </summary>
public class GetDailyConsolidatedQueryHandlerTests
{
    private readonly IConsolidatedCacheService _cacheService = Substitute.For<IConsolidatedCacheService>();
    private readonly IDailyConsolidatedRepository _repository = Substitute.For<IDailyConsolidatedRepository>();
    private readonly ILogger<GetDailyConsolidatedQueryHandler> _logger = Substitute.For<ILogger<GetDailyConsolidatedQueryHandler>>();
    private readonly GetDailyConsolidatedQueryHandler _handler;

    /// <summary>
    /// Instancia o manipulador de consulta com os dubles de teste de infraestrutura.
    /// </summary>
    public GetDailyConsolidatedQueryHandlerTests()
    {
        _handler = new GetDailyConsolidatedQueryHandler(_cacheService, _repository, _logger);
    }

    /// <summary>
    /// Valida que em caso de presenca da chave no Redis (Cache Hit), o valor e retornado imediatamente
    /// com flag Cached=true e o banco relacional PostgreSQL jamais e acionado.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenInCache_ShouldReturnFromCacheWithoutHittingDatabase()
    {
        // Arrange - Simulacao de consolidado existente no cache Redis
        var date = new DateOnly(2026, 9, 4);
        var query = new GetDailyConsolidatedQuery("MERCH_01", date);

        var cachedEntity = new DailyConsolidated("MERCH_01", date);
        cachedEntity.ApplyTransaction(300m, "Credit");
        cachedEntity.ApplyTransaction(100m, "Debit");

        _cacheService.GetAsync("MERCH_01", date, Arg.Any<CancellationToken>())
            .Returns(cachedEntity);

        // Act - Execucao da consulta
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert - A resposta deve refletir o estado do cache com performance otimizada
        result.Should().NotBeNull();
        result.MerchantId.Should().Be("MERCH_01");
        result.TotalCredits.Should().Be(300m);
        result.TotalDebits.Should().Be(100m);
        result.ClosingBalance.Should().Be(200m);
        result.Cached.Should().BeTrue();

        // Garante isolamento: o banco relacional NUNCA foi consultado sob Cache Hit
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default!, default, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Valida que em caso de ausencia da chave no Redis (Cache Miss), o manipulador consulta o PostgreSQL
    /// e imediatamente popula o cache Redis para otimizar chamadas subsequentes.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WhenCacheMiss_ShouldFetchFromDatabaseAndPopulateCache()
    {
        // Arrange - Simulacao de Cache Miss no Redis e presenca no banco relacional
        var date = new DateOnly(2026, 9, 4);
        var query = new GetDailyConsolidatedQuery("MERCH_01", date);

        var dbEntity = new DailyConsolidated("MERCH_01", date);
        dbEntity.ApplyTransaction(500m, "Credit");

        _cacheService.GetAsync("MERCH_01", date, Arg.Any<CancellationToken>())
            .Returns((DailyConsolidated?)null);

        _repository.GetAsync("MERCH_01", date, Arg.Any<CancellationToken>())
            .Returns(dbEntity);

        // Act - Execucao da consulta sob Cache Miss
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert - O resultado deve vir do banco com Cached=false e o Redis deve ser populado
        result.Should().NotBeNull();
        result.ClosingBalance.Should().Be(500m);
        result.Cached.Should().BeFalse();

        await _repository.Received(1).GetAsync("MERCH_01", date, Arg.Any<CancellationToken>());
        await _cacheService.Received(1).SetAsync(dbEntity, cancellationToken: Arg.Any<CancellationToken>());
    }
}
