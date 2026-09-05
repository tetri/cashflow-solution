# Original User Request

## 2026-09-05T01:17:22Z

Construção de uma solução completa, escalável e resiliente em .NET 8 para controle de fluxo de caixa (Serviço de Lançamentos e Serviço de Consolidado Diário) seguindo rigorosamente Clean Architecture, CQRS, Arquitetura Orientada a Eventos (RabbitMQ), PostgreSQL, Redis e Polly.

Todo o código-fonte deve ser exaustivamente comentado em cada classe e método, explicando detalhadamente a lógica de negócio e as decisões arquiteturais. Toda a documentação e artefatos devem ser estritamente em português do Brasil (pt-BR) em linguagem natural e fluida, sem a presença de emojis. A solução deve conter regras estritas de linting (.editorconfig e Roslyn Analyzers), histórico de Conventional Commits em português do Brasil, e repositório Git privado inicial criado e configurado via GitHub CLI.

Working directory: E:\Sandbox\sdfghjytrews
Integrity mode: development

## Requirements

### R1. Arquitetura e Implementação dos Serviços (.NET 8)
- Implementar o **Serviço de Lançamentos** (Write-side / Command) gravando débitos/créditos no PostgreSQL e publicando eventos assíncronos no RabbitMQ.
- Implementar o **Serviço de Consolidado Diário** (Read-side / Query) servindo consultas de alta performance via Redis (<5ms, suportando >50 RPS) com fallback resiliente para o PostgreSQL.
- Implementar o **Consolidated Worker** para consumo assíncrono idempotente dos eventos de transação e consolidação contínua de saldo.
- Configurar resiliência com Polly (Retry com backoff exponencial + jitter, Circuit Breaker e Dead Letter Queue).

### R2. Comentários Arquiteturais e Didática no Código-Fonte
- Cada arquivo de código-fonte (.cs) deve conter comentários detalhados e explicativos em cada bloco de código, método e classe, justificando as decisões técnicas (ex.: por que usar idempotência, por que usar concorrência otimista, por que estruturar comandos e consultas separadamente).

### R3. Idioma, Estilo e Restrição de Emojis
- Toda a documentação (README, ADRs, especificações técnicas, mensagens de commit, mensagens de log e documentação Swagger) deve ser redigida em português do Brasil (pt-BR) culto e natural.
- **Restrição Estrita:** Não utilizar nenhum emoji em nenhum arquivo, commit, comentário ou documento do projeto.

### R4. Qualidade de Código e Linting Rigoroso
- Adicionar `.editorconfig` com padrões rígidos de formatação e boas práticas do C# 12 / .NET 8.
- Habilitar análise estática de código com warnings tratados como erros (`TreatWarningsAsErrors` ativado para regras de estilo essenciais).

### R5. Controle de Versão e Repositório Remoto Privado
- Inicializar repositório Git local estruturado com commits atômicos seguindo o padrão Conventional Commits em português (ex.: `feat: adicionar manipulador de comando de lancamento`, `chore: configurar regras de linting no editorconfig`).
- Configurar a criação do repositório remoto privado via GitHub CLI (`gh repo create --private`) se o ambiente estiver autenticado.

### R6. Suíte de Testes Automatizados
- Implementar testes unitários e de integração cobrindo cenários de sucesso, validação de regras de domínio, resiliência e fallback usando xUnit, FluentAssertions e NSubstitute.

### R7. Orquestração Local com Docker
- Fornecer `docker-compose.yml` e `Dockerfile` para cada serviço, permitindo a inicialização completa do ecossistema (APIs, Worker, PostgreSQL com init script, Redis e RabbitMQ).

## Acceptance Criteria

### Restrições de Estilo e Documentação
- [ ] 0 emojis em todo o repositório (validável programaticamente).
- [ ] 100% dos textos, comentários e documentações em português do Brasil (pt-BR).
- [ ] Compilação de toda a solution com sucesso via `dotnet build`.

### Funcionalidade e Resiliência
- [ ] Inserção de lançamentos desacoplada com publicação resiliente no RabbitMQ.
- [ ] Consumo idempotente de eventos pelo worker sem duplicação de saldo.
- [ ] Leitura do consolidado servida a partir do Redis em tempo submilisegundo.
- [ ] Resiliência com Polly ativa e tratativa de mensagens inválidas via Dead Letter Queue.
- [ ] Todos os testes unitários e de integração executando e passando via `dotnet test`.
