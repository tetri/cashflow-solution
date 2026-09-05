// Ponto de entrada da API de Lancamentos (Transactions API - Write-Side)
// Responsavel por expor os endpoints de registro de debitos e creditos financeiros.

var builder = WebApplication.CreateBuilder(args);

// Adiciona documentacao Swagger OpenAPI em portugues
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CashFlow - Servico de Lancamentos (Write-Side)",
        Version = "v1",
        Description = "API REST de alta resiliencia para recepcao e persistencia de transacoes financeiras."
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CashFlow Transactions API v1");
    });
}

// Endpoint de verificacao de saude operacional
app.MapGet("/health", () => Results.Ok(new { status = "Saudavel", servico = "CashFlow.Transactions.Api", horarioUtc = DateTime.UtcNow }))
   .WithName("VerificarSaude")
   .WithTags("Monitoramento");

app.Run();
