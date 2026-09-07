using CashFlow.Consolidated.Application;
using CashFlow.Consolidated.Application.Common;
using CashFlow.Consolidated.Application.Queries.GetDailyConsolidated;
using CashFlow.Consolidated.Infrastructure;
using CashFlow.Shared.Domain.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

// Ponto de entrada da API de Consolidado Diario (Consolidated API - Read-Side)
// Responsavel por servir consultas de saldo consolidado com latencia sub-5ms via Redis,
// fallback transparente para PostgreSQL via Polly, documentacao Swagger em portugues culto
// e conformidade rigorosa com diretrizes de seguranca OWASP API Security Top 10.

var builder = WebApplication.CreateBuilder(args);

// Configuracao de seguranca da API lida de variaveis de ambiente ou configuracao (.env)
var chaveApiKeyEsperada = builder.Configuration["Authentication:ApiKey"]
    ?? builder.Configuration["API_KEY"]
    ?? "cashflow-secret-api-key-2026";

// Configuracao de documentacao Swagger OpenAPI em portugues culto com esquemas de seguranca OWASP
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CashFlow - Servico de Consolidado Diario (Read-Side)",
        Version = "v1",
        Description = "API REST de alta performance para consulta do saldo consolidado diario por comerciante, suportando mais de 50 requisicoes por segundo via cache distribuido Redis com seguranca OWASP."
    });

    // Definicao de seguranca Swagger: Autenticacao por cabecalho X-Api-Key
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Chave de seguranca da API via cabecalho HTTP X-Api-Key (Exemplo: cashflow-secret-api-key-2026)",
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "ApiKeyScheme"
    });

    // Definicao de seguranca Swagger: Autenticacao por Bearer Token
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Token de autenticacao Bearer (Exemplo: cashflow-secret-api-key-2026)",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
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

// Middleware OWASP 1: Cabecalhos de seguranca HTTP (Defense in Depth / OWASP Secure Headers)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var ehSwagger = path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);

    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");

    // No Swagger UI, permitimos scripts/estilos inline e esquemas data: para correta renderizacao da interface grafica
    if (ehSwagger)
    {
        context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:;");
    }
    else
    {
        context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'");
    }

    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    context.Response.Headers.Append("X-Permitted-Cross-Domain-Policies", "none");
    await next();
});

// Middleware OWASP 2: Autenticacao e controle de acesso (OWASP API1 e API2)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    // Endpoints publicos liberados sem autenticacao: Health Check e documentacao Swagger
    if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    // Inspecao de credenciais nos cabecalhos X-Api-Key ou Authorization Bearer
    string? tokenFornecido = null;
    if (context.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
    {
        tokenFornecido = apiKeyHeader.ToString().Trim();
    }
    else if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        var authStr = authHeader.ToString().Trim();
        if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            tokenFornecido = authStr["Bearer ".Length..].Trim();
        }
    }

    if (string.IsNullOrWhiteSpace(tokenFornecido) ||
        !string.Equals(tokenFornecido, chaveApiKeyEsperada, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";

        var problema = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
            Title = "Acesso nao autorizado",
            Status = StatusCodes.Status401Unauthorized,
            Detail = "Credenciais de autenticacao ausentes ou invalidas. Forneca o cabecalho 'X-Api-Key' ou 'Authorization: Bearer <token>' valido.",
            Instance = context.Request.Path
        };
        problema.Extensions["errorCode"] = CashFlowErrorCodes.AutenticacaoNaoAutorizada;
        problema.Extensions["timestamp"] = DateTime.UtcNow;

        await context.Response.WriteAsJsonAsync(problema);
        return;
    }

    await next();
});

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
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Parametro invalido",
            Status = StatusCodes.Status400BadRequest,
            Detail = "O identificador do comerciante e a data nao podem conter emojis ou simbolos graficos especiais."
        };
        problemDetails.Extensions["errorCode"] = CashFlowErrorCodes.ConsolidadoParametroInvalido;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

        return Results.BadRequest(problemDetails);
    }

    // Validacao 2: Comprimento do identificador do comerciante
    if (string.IsNullOrWhiteSpace(merchantId) || merchantId.Length > 50)
    {
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Identificador de comerciante invalido",
            Status = StatusCodes.Status400BadRequest,
            Detail = "O identificador do comerciante deve ser preenchido e conter no maximo 50 caracteres."
        };
        problemDetails.Extensions["errorCode"] = CashFlowErrorCodes.ConsolidadoParametroInvalido;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

        return Results.BadRequest(problemDetails);
    }

    // Validacao 3: Formato estrito da data contábil (yyyy-MM-dd)
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var dataContabil))
    {
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Formato de data invalido",
            Status = StatusCodes.Status400BadRequest,
            Detail = $"A data informada '{date}' nao corresponde ao padrao esperado 'yyyy-MM-dd' (exemplo: 2026-09-05)."
        };
        problemDetails.Extensions["errorCode"] = CashFlowErrorCodes.ConsolidadoDataInvalida;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

        return Results.BadRequest(problemDetails);
    }

    // Execucao do manipulador de consulta CQRS com leitura otimizada em cache Redis e fallback relacional
    var query = new GetDailyConsolidatedQuery(merchantId.Trim(), dataContabil);
    var resultado = await handler.HandleAsync(query, cancellationToken);

    return Results.Ok(resultado);
})
.WithName("ObterConsolidadoDiario")
.WithTags("Consolidado")
.Produces<DailyConsolidatedDto>(StatusCodes.Status200OK)
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

app.Run();
