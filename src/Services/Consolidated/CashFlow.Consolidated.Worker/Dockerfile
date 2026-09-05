# Estagio 1: Compilacao e publicacao do worker .NET 8
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copia arquivos de solucao e configuracoes centrais de compilacao
COPY CashFlow.sln Directory.Build.props .editorconfig ./

# Copia arquivos de projeto para otimizacao de cache de camadas do Docker
COPY src/Shared/CashFlow.Shared.Domain/CashFlow.Shared.Domain.csproj src/Shared/CashFlow.Shared.Domain/
COPY src/Services/Consolidated/CashFlow.Consolidated.Domain/CashFlow.Consolidated.Domain.csproj src/Services/Consolidated/CashFlow.Consolidated.Domain/
COPY src/Services/Consolidated/CashFlow.Consolidated.Infrastructure/CashFlow.Consolidated.Infrastructure.csproj src/Services/Consolidated/CashFlow.Consolidated.Infrastructure/
COPY src/Services/Consolidated/CashFlow.Consolidated.Worker/CashFlow.Consolidated.Worker.csproj src/Services/Consolidated/CashFlow.Consolidated.Worker/

# Restaura dependencias NuGet do projeto do Worker
RUN dotnet restore src/Services/Consolidated/CashFlow.Consolidated.Worker/CashFlow.Consolidated.Worker.csproj

# Copia todo o codigo-fonte relevante
COPY src/Shared/CashFlow.Shared.Domain/ src/Shared/CashFlow.Shared.Domain/
COPY src/Services/Consolidated/ src/Services/Consolidated/

# Compila e publica o worker em modo Release
RUN dotnet publish src/Services/Consolidated/CashFlow.Consolidated.Worker/CashFlow.Consolidated.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Estagio 2: Imagem final enxuta de execucao (Runtime .NET Generic Host)
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS runtime
WORKDIR /app

# Adiciona usuario nao-root para execucao segura do worker
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build /app/publish .

ENV DOTNET_EnableDiagnostics=0

ENTRYPOINT ["dotnet", "CashFlow.Consolidated.Worker.dll"]
