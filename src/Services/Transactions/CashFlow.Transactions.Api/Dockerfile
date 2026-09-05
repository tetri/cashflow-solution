# Estagio 1: Compilacao e publicacao da aplicacao .NET 8
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copia arquivos de solucao e configuracoes centrais de compilacao
COPY CashFlow.sln Directory.Build.props .editorconfig ./

# Copia arquivos de projeto individualmente para otimizacao de cache de camadas do Docker
COPY src/Shared/CashFlow.Shared.Domain/CashFlow.Shared.Domain.csproj src/Shared/CashFlow.Shared.Domain/
COPY src/Services/Transactions/CashFlow.Transactions.Domain/CashFlow.Transactions.Domain.csproj src/Services/Transactions/CashFlow.Transactions.Domain/
COPY src/Services/Transactions/CashFlow.Transactions.Application/CashFlow.Transactions.Application.csproj src/Services/Transactions/CashFlow.Transactions.Application/
COPY src/Services/Transactions/CashFlow.Transactions.Infrastructure/CashFlow.Transactions.Infrastructure.csproj src/Services/Transactions/CashFlow.Transactions.Infrastructure/
COPY src/Services/Transactions/CashFlow.Transactions.Api/CashFlow.Transactions.Api.csproj src/Services/Transactions/CashFlow.Transactions.Api/

# Restaura dependencias NuGet do projeto da API de Lancamentos
RUN dotnet restore src/Services/Transactions/CashFlow.Transactions.Api/CashFlow.Transactions.Api.csproj

# Copia todo o codigo-fonte relevante
COPY src/Shared/CashFlow.Shared.Domain/ src/Shared/CashFlow.Shared.Domain/
COPY src/Services/Transactions/ src/Services/Transactions/

# Compila e publica a aplicacao em modo Release com otimizacoes ativas
RUN dotnet publish src/Services/Transactions/CashFlow.Transactions.Api/CashFlow.Transactions.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Estagio 2: Imagem final enxuta de execucao (Runtime ASP.NET Core)
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

# Adiciona usuario nao-root para execucao segura do container
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build /app/publish .

# Configuracoes de ambiente para execucao no container
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "CashFlow.Transactions.Api.dll"]
