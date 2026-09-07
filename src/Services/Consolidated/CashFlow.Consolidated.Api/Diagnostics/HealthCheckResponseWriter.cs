using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CashFlow.Consolidated.Api.Diagnostics;

/// <summary>
/// Formatador de respostas HTTP JSON para os endpoints de verificacao de saude.
/// </summary>
public static class HealthCheckResponseWriter
{
    public static readonly string[] TagsVivacidade = ["live"];
    public static readonly string[] TagsProntidao = ["ready"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static Task WriteDetailedResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status == HealthStatus.Healthy ? "Saudavel" : "Degradado",
            servico = "CashFlow.Consolidated.Api",
            duracaoTotalMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            horarioUtc = DateTime.UtcNow,
            dependencias = report.Entries.Select(entry => new
            {
                componente = entry.Key,
                status = entry.Value.Status == HealthStatus.Healthy ? "Saudavel" : "Indisponivel",
                duracaoMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                descricao = entry.Value.Description,
                erro = entry.Value.Exception?.Message
            })
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return context.Response.WriteAsync(json);
    }
}
