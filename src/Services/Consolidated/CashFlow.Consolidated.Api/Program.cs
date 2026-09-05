using CashFlow.Consolidated.Application;
using CashFlow.Consolidated.Application.Common;
using CashFlow.Consolidated.Application.Queries.GetDailyConsolidated;
using CashFlow.Consolidated.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

// Ponto de entrada da API de Consolidado Diario (Consolidated API - Read-Side)
// Responsavel por servir consultas de saldo consolidado com latencia sub-5ms via Redis,
// fallback transparente para PostgreSQL via Polly, e documentacao Swagger em portugues culto.

var builder = WebApplication.CreateBuilder(args);

// Configuracao de documentacao Swagger OpenAPI em portugues culto
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CashFlow - Servico de Consolidado Diario (Read-Side)",
        Version = "v1",
        Description = "API REST de alta performance para consulta do saldo consolidado diario por comerciante, suportando mais de 50 requisicoes por segundo via cache distribuido Redis."
    });
});

// Registro da conexao compartilhada com o Redis para cache
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis")
        ?? "localhost:6379,abortConnect=false";

    var logger = sp.GetRequiredService<ILogger<IConnectionMultiplexer>>();
    logger.LogInformation("Estabelecendo multiplexador de conexao com Redis em {ConnectionString}...", connectionString);

    return ConnectionMultiplexer.Connect(connectionString);
});

// Registro dos servicos das camadas de Aplicacao e Infraestrutura do Consolidado
builder.Services.AddConsolidatedApplication();
builder.Services.AddConsolidatedInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CashFlow Consolidated API v1");
    });
}

// Endpoint de verificacao de saude operacional
app.MapGet("/health", () => Results.Ok(new
{
    status = "Saudavel",
    servico = "CashFlow.Consolidated.Api",
    horarioUtc = DateTime.UtcNow
}))
.WithName("VerificarSaude")
.WithTags("Monitoramento");

// Endpoint principal para obtencao do saldo consolidado diario por comerciante e data
app.MapGet("/api/v1/consolidated/{merchantId}/{date}", async (
    string merchantId,
    string date,
    GetDailyConsolidatedQueryHandler handler,
    CancellationToken cancellationToken) =>
{
    // Validacao 1: Inspecao rigorosa de ausencia de emojis (Regra R3)
    if (EmojiDetector.ContemEmoji(merchantId) || EmojiDetector.ContemEmoji(date))
    {
        return Results.BadRequest(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Parametro invalido",
            Status = StatusCodes.Status400BadRequest,
            Detail = "O identificador do comerciante e a data nao podem conter emojis ou simbolos graficos especiais."
        });
    }

    // Validacao 2: Comprimento do identificador do comerciante
    if (string.IsNullOrWhiteSpace(merchantId) || merchantId.Length > 50)
    {
        return Results.BadRequest(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Identificador de comerciante invalido",
            Status = StatusCodes.Status400BadRequest,
            Detail = "O identificador do comerciante deve ser preenchido e conter no maximo 50 caracteres."
        });
    }

    // Validacao 3: Formato estrito da data contábil (yyyy-MM-dd)
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var dataContabil))
    {
        return Results.BadRequest(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Formato de data invalido",
            Status = StatusCodes.Status400BadRequest,
            Detail = $"A data informada '{date}' nao corresponde ao padrao esperado 'yyyy-MM-dd' (exemplo: 2026-09-05)."
        });
    }

    // Execucao do manipulador de consulta CQRS com leitura otimizada em cache Redis e fallback relacional
    var query = new GetDailyConsolidatedQuery(merchantId.Trim(), dataContabil);
    var resultado = await handler.HandleAsync(query, cancellationToken);

    return Results.Ok(resultado);
})
.WithName("ObterConsolidadoDiario")
.WithTags("Consolidado")
.Produces<DailyConsolidatedDto>(StatusCodes.Status200OK)
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

app.Run();
