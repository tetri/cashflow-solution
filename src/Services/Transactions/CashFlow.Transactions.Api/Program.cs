using CashFlow.Shared.Domain.Errors;
using CashFlow.Transactions.Api.Models;
using CashFlow.Transactions.Application;
using CashFlow.Transactions.Application.Commands.CreateTransaction;
using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Domain.Interfaces;
using CashFlow.Transactions.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

// Ponto de entrada da API de Lancamentos (Transactions API - Write-Side)
// Responsavel por expor os endpoints HTTP REST para recepcao de debitos e creditos,
// validacao de idempotencia via cabecalho ou corpo, persistencia e publicacao de eventos,
// alem de aplicar conformidade rigorosa com padroes OWASP API Security (Headers, Autenticacao e ErrorCodes).

var builder = WebApplication.CreateBuilder(args);

// Configuracao de seguranca da API lida de variaveis de ambiente ou configuracao (.env)
var chaveApiKeyEsperada = builder.Configuration["Authentication:ApiKey"]
    ?? builder.Configuration["API_KEY"]
    ?? "cashflow-secret-api-key-2026";

// Configuracao de documentacao Swagger/OpenAPI em portugues culto com esquemas de autenticacao OWASP
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CashFlow - Servico de Lancamentos (Write-Side)",
        Version = "v1",
        Description = "API REST de alta resiliencia e baixa latencia para registro transacional de operacoes financeiras com suporte estrito a idempotencia, mensageria assincrona e seguranca OWASP API Top 10."
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

// Registro dos servicos das camadas de Aplicacao e Infraestrutura (Clean Architecture)
builder.Services.AddTransactionsApplication();
builder.Services.AddTransactionsInfrastructure(builder.Configuration);

var app = builder.Build();

// Middleware OWASP 1: Cabecalhos de seguranca HTTP (Defense in Depth / OWASP Secure Headers)
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'");
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
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CashFlow Transactions API v1");
    });
}

// Endpoint de verificacao de saude operacional do servico
app.MapGet("/health", () => Results.Ok(new
{
    status = "Saudavel",
    servico = "CashFlow.Transactions.Api",
    horarioUtc = DateTime.UtcNow
}))
.WithName("VerificarSaude")
.WithTags("Monitoramento");

// Endpoint de consulta de transacao por identificador unico universal
app.MapGet("/api/v1/transactions/{id:guid}", async (
    Guid id,
    ITransactionRepository repository,
    CancellationToken cancellationToken) =>
{
    var transacao = await repository.GetByIdAsync(id, cancellationToken);
    if (transacao is null)
    {
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            Title = "Transacao nao encontrada",
            Status = StatusCodes.Status404NotFound,
            Detail = $"Nenhuma transacao financeira foi localizada para o identificador {id}."
        };
        problemDetails.Extensions["errorCode"] = CashFlowErrorCodes.TransacaoNaoEncontrada;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

        return Results.NotFound(problemDetails);
    }

    return Results.Ok(new
    {
        transactionId = transacao.Id,
        merchantId = transacao.MerchantId,
        amount = transacao.Amount,
        type = transacao.Type.ToString(),
        description = transacao.Description,
        createdAt = transacao.CreatedAt
    });
})
.WithName("ObterTransacaoPorId")
.WithTags("Transacoes")
.Produces(StatusCodes.Status200OK)
.Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
.Produces<ProblemDetails>(StatusCodes.Status404NotFound);

// Endpoint principal para registro de lancamentos financeiros (Credito / Debito) com idempotencia estrita
app.MapPost("/api/v1/transactions", async (
    [FromBody] RequisicaoLancamentoDto requisicao,
    HttpContext httpContext,
    CreateTransactionCommandHandler handler,
    CancellationToken cancellationToken) =>
{
    // Precedencia de chave de idempotencia: cabecalho HTTP X-Idempotency-Key sobrepoe o corpo da requisicao
    string? chaveIdempotencia = null;
    if (httpContext.Request.Headers.TryGetValue("X-Idempotency-Key", out var valorCabecalho) &&
        !string.IsNullOrWhiteSpace(valorCabecalho))
    {
        chaveIdempotencia = valorCabecalho.ToString().Trim();
    }
    else if (!string.IsNullOrWhiteSpace(requisicao.IdempotencyKey))
    {
        chaveIdempotencia = requisicao.IdempotencyKey.Trim();
    }

    var comando = new CreateTransactionCommand(
        requisicao.MerchantId,
        requisicao.Amount,
        requisicao.Type,
        requisicao.Description,
        chaveIdempotencia
    );

    try
    {
        var resultado = await handler.HandleAsync(comando, cancellationToken);

        var resposta = new RespostaLancamentoDto(
            resultado.TransactionId,
            resultado.MerchantId,
            resultado.Amount,
            resultado.Type,
            resultado.CreatedAt
        );

        // Se a transacao for reconhecida como duplicata idempotente integra, retorna 200 OK
        if (resultado.IsIdempotentDuplicate)
        {
            return Results.Ok(resposta);
        }

        // Se for uma nova transacao registrada com sucesso, retorna 201 Created com cabecalho Location
        return Results.Created($"/api/v1/transactions/{resultado.TransactionId}", resposta);
    }
    catch (ArgumentException ex)
    {
        // Violacao de regras de validacao ou deteccao de emojis: HTTP 400 Bad Request RFC 7231 / RFC 7807
        var codigoErro = ex.Message switch
        {
            var m when m.Contains("maior que zero", StringComparison.OrdinalIgnoreCase) => CashFlowErrorCodes.TransacaoValorInvalido,
            var m when m.Contains("MerchantId", StringComparison.OrdinalIgnoreCase) => CashFlowErrorCodes.TransacaoComercianteInvalido,
            var m when m.Contains("descricao", StringComparison.OrdinalIgnoreCase) => CashFlowErrorCodes.TransacaoDescricaoInvalida,
            var m when m.Contains("tipo de transacao", StringComparison.OrdinalIgnoreCase) => CashFlowErrorCodes.TransacaoTipoInvalido,
            _ => CashFlowErrorCodes.TransacaoParametroInvalido
        };

        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Erro de validacao nos dados do lancamento",
            Status = StatusCodes.Status400BadRequest,
            Detail = ex.Message
        };
        problemDetails.Extensions["errorCode"] = codigoErro;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

        return Results.BadRequest(problemDetails);
    }
    catch (IdempotencyConflictException ex)
    {
        // Conflito de negocio por reutilizacao de chave com dados divergentes: HTTP 409 Conflict RFC 7231 / RFC 7807
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            Title = "Conflito de idempotencia na transacao",
            Status = StatusCodes.Status409Conflict,
            Detail = ex.Message
        };
        problemDetails.Extensions["errorCode"] = CashFlowErrorCodes.TransacaoIdempotenciaConflito;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

        return Results.Conflict(problemDetails);
    }
})
.WithName("RegistrarTransacao")
.WithTags("Transacoes")
.Produces<RespostaLancamentoDto>(StatusCodes.Status201Created)
.Produces<RespostaLancamentoDto>(StatusCodes.Status200OK)
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
.Produces<ProblemDetails>(StatusCodes.Status409Conflict);

app.Run();
