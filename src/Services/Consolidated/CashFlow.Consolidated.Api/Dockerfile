# Estagio 1: Compilacao e publicacao da aplicacao .NET 8
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copia arquivos de solucao e configuracoes centrais de compilacao
COPY CashFlow.sln Directory.Build.props .editorconfig ./

# Copia arquivos de projeto para otimizacao de cache de camadas do Docker
COPY src/Services/Consolidated/CashFlow.Consolidated.Domain/CashFlow.Consolidated.Domain.csproj src/Services/Consolidated/CashFlow.Consolidated.Domain/
COPY src/Services/Consolidated/CashFlow.Consolidated.Application/CashFlow.Consolidated.Application.csproj src/Services/Consolidated/CashFlow.Consolidated.Application/
COPY src/Services/Consolidated/CashFlow.Consolidated.Infrastructure/CashFlow.Consolidated.Infrastructure.csproj src/Services/Consolidated/CashFlow.Consolidated.Infrastructure/
COPY src/Services/Consolidated/CashFlow.Consolidated.Api/CashFlow.Consolidated.Api.csproj src/Services/Consolidated/CashFlow.Consolidated.Api/

# Restaura dependencias NuGet do projeto da API de Consolidado
RUN dotnet restore src/Services/Consolidated/CashFlow.Consolidated.Api/CashFlow.Consolidated.Api.csproj

# Copia todo o codigo-fonte relevante
COPY src/Services/Consolidated/ src/Services/Consolidated/

# Compila e publica a aplicacao em modo Release
RUN dotnet publish src/Services/Consolidated/CashFlow.Consolidated.Api/CashFlow.Consolidated.Api.csproj \
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

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "CashFlow.Consolidated.Api.dll"]
