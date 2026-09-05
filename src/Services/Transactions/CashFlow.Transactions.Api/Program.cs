using CashFlow.Transactions.Api.Models;
using CashFlow.Transactions.Application;
using CashFlow.Transactions.Application.Commands.CreateTransaction;
using CashFlow.Transactions.Application.Exceptions;
using CashFlow.Transactions.Domain.Interfaces;
using CashFlow.Transactions.Infrastructure;
using Microsoft.AspNetCore.Mvc;

// Ponto de entrada da API de Lancamentos (Transactions API - Write-Side)
// Responsavel por expor os endpoints HTTP REST para recepcao de debitos e creditos,
// validacao de idempotencia via cabecalho ou corpo, persistencia e publicacao de eventos.

var builder = WebApplication.CreateBuilder(args);

// Configuracao de documentacao Swagger/OpenAPI em portugues culto
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CashFlow - Servico de Lancamentos (Write-Side)",
        Version = "v1",
        Description = "API REST de alta resiliencia e baixa latencia para registro transacional de operacoes financeiras com suporte estrito a idempotencia e mensageria assincrona."
    });
});

// Registro dos servicos das camadas de Aplicacao e Infraestrutura (Clean Architecture)
builder.Services.AddTransactionsApplication();
builder.Services.AddTransactionsInfrastructure(builder.Configuration);

var app = builder.Build();

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
        return Results.NotFound(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            Title = "Transacao nao encontrada",
            Status = StatusCodes.Status404NotFound,
            Detail = $"Nenhuma transacao financeira foi localizada para o identificador {id}."
        });
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
.WithTags("Transacoes");

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
        // Violacao de regras de validacao ou deteccao de emojis: HTTP 400 Bad Request RFC 7231
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Erro de validacao nos dados do lancamento",
            Status = StatusCodes.Status400BadRequest,
            Detail = ex.Message
        };

        return Results.BadRequest(problemDetails);
    }
    catch (IdempotencyConflictException ex)
    {
        // Conflito de negocio por reutilizacao de chave com dados divergentes: HTTP 409 Conflict RFC 7231
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            Title = "Conflito de idempotencia na transacao",
            Status = StatusCodes.Status409Conflict,
            Detail = ex.Message
        };

        return Results.Conflict(problemDetails);
    }
})
.WithName("RegistrarTransacao")
.WithTags("Transacoes")
.Produces<RespostaLancamentoDto>(StatusCodes.Status201Created)
.Produces<RespostaLancamentoDto>(StatusCodes.Status200OK)
.Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
.Produces<ProblemDetails>(StatusCodes.Status409Conflict);

app.Run();
