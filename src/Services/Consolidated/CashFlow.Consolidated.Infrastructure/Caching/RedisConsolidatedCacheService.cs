using System.Text.Json;
using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CashFlow.Consolidated.Infrastructure.Caching;

/// <summary>
/// Implementacao do servico de cache distribuido utilizando StackExchange.Redis.
/// Esta classe gerencia a serializacao em JSON compacto UTF-8 e a aplicacao
/// de Time-To-Live (TTL) diferenciado por data, alem de capturar falhas operacionais
/// para viabilizar o fallback transparente da aplicacao para o PostgreSQL.
/// </summary>
public class RedisConsolidatedCacheService : IConsolidatedCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisConsolidatedCacheService> _logger;

    /// <summary>
    /// Inicializa o servico de cache obtendo a conexao gerenciada com o cluster/instancia Redis.
    /// </summary>
    /// <param name="redis">Multiplexador de conexao Redis.</param>
    /// <param name="logger">Mecanismo de telemetria.</param>
    public RedisConsolidatedCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisConsolidatedCacheService> logger)
    {
        _redis = redis;
        _database = _redis.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// Constroi a chave padronizada do Redis no formato: consolidated:{merchantId}:{yyyy-MM-dd}.
    /// </summary>
    private static string BuildKey(string merchantId, DateOnly date) =>
        $"consolidated:{merchantId}:{date:yyyy-MM-dd}";

    /// <summary>
    /// Consulta uma chave no Redis de forma ultra-rapida.
    /// Em caso de falha de socket, timeout ou conexao, retorna nulo para disparar
    /// o fallback suave para o banco de dados sem propagar erro 500 para a API.
    /// </summary>
    public async Task<DailyConsolidated?> GetAsync(string merchantId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(merchantId, date);
        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<DailyConsolidated>((string)value!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na comunicacao com o Redis para chave {Key}. Habilitando fallback gracioso.", key);
            return null; // Permite o fallback graceful para o banco relacional
        }
    }

    /// <summary>
    /// Grava a entidade de consolidado no Redis com politica de TTL inteligente:
    /// - Data Corrente (Hoje): TTL de 1 hora (renovado continuamente a cada transacao).
    /// - Datas Passadas: TTL de 24 horas (balanco retroativo imutavel).
    /// </summary>
    public async Task SetAsync(DailyConsolidated consolidated, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(consolidated.MerchantId, consolidated.Date);
        var effectiveTtl = ttl ?? (consolidated.Date == DateOnly.FromDateTime(DateTime.UtcNow)
            ? TimeSpan.FromHours(1)
            : TimeSpan.FromHours(24));

        try
        {
            var json = JsonSerializer.Serialize(consolidated);
            await _database.StringSetAsync(key, json, effectiveTtl);
            _logger.LogDebug("Cache atualizado para chave {Key} com TTL {TTL}", key, effectiveTtl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao persistir chave {Key} no Redis.", key);
        }
    }

    /// <summary>
    /// Remove uma chave do cache em caso de necessidade de reprocessamento ou invalidacao forçada.
    /// </summary>
    public async Task RemoveAsync(string merchantId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(merchantId, date);
        try
        {
            await _database.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao invalidar chave {Key} no Redis.", key);
        }
    }
}
