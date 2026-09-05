// Ponto de entrada da API de Consolidado Diario (Consolidated API - Read-Side)
// Responsavel por servir consultas de saldo consolidado com latencia sub-5ms via Redis.

var builder = WebApplication.CreateBuilder(args);

// Adiciona documentacao Swagger OpenAPI em portugues
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CashFlow - Servico de Consolidado Diario (Read-Side)",
        Version = "v1",
        Description = "API REST otimizada para consultas de saldos diarios consolidados por comerciante."
    });
});

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
app.MapGet("/health", () => Results.Ok(new { status = "Saudavel", servico = "CashFlow.Consolidated.Api", horarioUtc = DateTime.UtcNow }))
   .WithName("VerificarSaude")
   .WithTags("Monitoramento");

app.Run();
