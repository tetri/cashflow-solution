# Project: CashFlow Solution - Controle de Fluxo de Caixa

Arquitetura em microsserviços orientada a eventos em .NET 8 com Clean Architecture, CQRS, PostgreSQL, Redis, RabbitMQ e Polly, com zero emojis e estritamente em português do Brasil (pt-BR).

## Architecture
A solução adota segregação de responsabilidade entre comandos e consultas (CQRS) e comunicação assíncrona orientada a eventos (EDA):
- **Write-Side (Serviço de Lançamentos):** API REST responsável por registrar débitos e créditos com idempotência estrita, persistir transações no PostgreSQL e publicar eventos `TransactionCreatedEvent` no RabbitMQ com políticas de resiliência Polly (Retry exponencial com jitter, Timeout e Circuit Breaker).
- **Consolidated Worker:** Daemon de segundo plano (`BackgroundService`) que consome eventos do RabbitMQ com prefetch=20 e confirmação manual (Ack/Nack), deduplica eventos via tabela `processed_events`, realiza a agregação atômica do saldo no PostgreSQL em transação única e renova o cache distribuído no Redis (Write-Through). Mensagens venenosas ou com falha persistente são roteadas para Dead Letter Queue (`cashflow.consolidated.transactions.dlq`).
- **Read-Side (Serviço de Consolidado Diário):** API REST otimizada para servir consultas de saldo consolidado por comerciante e data a partir do cache Redis (< 5ms, > 50 RPS). Em caso de cache miss ou abertura de disjuntor (Circuit Breaker) no Redis, executa fallback transparente para o PostgreSQL.

```
[Cliente / Comerciante] 
       |
       +---> POST /api/v1/transactions ---> [Transactions API]
       |                                          |
       |                                          +---> (PostgreSQL: transactions)
       |                                          |
       |                                          v (Publica TransactionCreatedEvent)
       |                                    [RabbitMQ: cashflow.events]
       |                                          |
       |                                          v (Consome com Idempotencia)
       |                                    [Consolidated Worker]
       |                                          |
       |                                          +---> (PostgreSQL: daily_consolidated & processed_events)
       |                                          |
       |                                          +---> (Redis: consolidated:{merchantId}:{date})
       |                                          |
       |                                          +---> [DLQ: cashflow.consolidated.transactions.dlq] (Falhas)
       |
       +---> GET /api/v1/consolidated/{merchantId}/{date} ---> [Consolidated API]
                                                                     |
                                                                     +---> [Redis Cache] (Hit: < 5ms)
                                                                     |
                                                                     +---> [PostgreSQL] (Miss ou Fallback)
```

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| F01 | Higienização de Emojis e pt-BR | Remoção de emojis do README e garantia de 100% dos textos, logs e códigos em pt-BR culto | M1 | ORIGINAL_REQUEST §R3 |
| F02 | Governança de Qualidade | Configuração de .editorconfig e Directory.Build.props com TreatWarningsAsErrors=true e C# 12 | M1 | ORIGINAL_REQUEST §R4 |
| F03 | Controle de Versão Git | Inicialização do Git (main), .gitignore completo, Conventional Commits em pt-BR e script GitHub CLI | M1 | ORIGINAL_REQUEST §R5 |
| F04 | Solution e Projetos .NET 8 | Criação de CashFlow.sln e dos 12 arquivos .csproj em Clean Architecture com dependências corretas | M1 | ORIGINAL_REQUEST §R1 |
| F05 | Registro de Crédito | Manipulador de comando para validar e persistir entradas financeiras no caixa | M2 | ORIGINAL_REQUEST §R1 |
| F06 | Registro de Débito | Manipulador de comando para validar e persistir saídas financeiras no caixa | M2 | ORIGINAL_REQUEST §R1 |
| F07 | Idempotência no Write-Side | Garantia de não duplicação de transações via IdempotencyKey e índice único no PostgreSQL | M2 | ORIGINAL_REQUEST §R1 |
| F08 | Persistência PostgreSQL (Transactions) | DbContext, mapeamento relacional e ITransactionRepository no PostgreSQL | M2 | ORIGINAL_REQUEST §R1 |
| F09 | API REST de Lançamentos | Endpoints HTTP POST /api/v1/transactions com Swagger em pt-BR, Program.cs e DI | M2 | ORIGINAL_REQUEST §R1 |
| F10 | Publicação Resiliente RabbitMQ | RabbitMqEventPublisher com Polly v8 (Retry exponencial, Jitter, Timeout 3s e Circuit Breaker) | M2 | ORIGINAL_REQUEST §R1 |
| F11 | Consumo Idempotente no Worker | TransactionEventConsumer com deduplicação contra processed_events e descarte gracioso | M3 | ORIGINAL_REQUEST §R1 |
| F12 | Agregação Atômica de Saldo | Cálculo e gravação atômica de saldo diário no PostgreSQL em transação única com versionamento | M3 | ORIGINAL_REQUEST §R1 |
| F13 | Dead Letter Queue (DLQ) | Configuração de DLX cashflow.events.dlx e fila DLQ para poison messages via BasicNack | M3 | ORIGINAL_REQUEST §R1 |
| F14 | Sincronização de Cache no Worker | Atualização imediata Write-Through no Redis com TTL inteligente (1h dia corrente / 24h histórico) | M3 | ORIGINAL_REQUEST §R1 |
| F15 | Persistência PostgreSQL (Consolidated) | DbContext, mapeamento relacional e IDailyConsolidatedRepository para Worker e API | M3 | ORIGINAL_REQUEST §R1 |
| F16 | API REST de Consolidado Diário | Endpoints HTTP GET /api/v1/consolidated/{merchantId}/{date} com Swagger em pt-BR | M4 | ORIGINAL_REQUEST §R1 |
| F17 | Caching Redis de Alta Performance | Consulta de saldo no Redis com latência sub-5ms e capacidade > 50 RPS | M4 | ORIGINAL_REQUEST §R1 |
| F18 | Fallback Resiliente de Leitura | Redirecionamento automático para o PostgreSQL sob cache miss ou falha no Redis via Polly | M4 | ORIGINAL_REQUEST §R1 |
| F19 | Comentários Didáticos Exaustivos | Comentários detalhados em todas as classes, métodos e blocos explicando razões arquiteturais | M1 a M5 | ORIGINAL_REQUEST §R2 |
| F20 | Suíte de Testes Unitários e Integração | Testes com xUnit, FluentAssertions e NSubstitute para todas as camadas e componentes | M5 | ORIGINAL_REQUEST §R6 |
| F21 | Orquestração Docker Compose | Dockerfiles multi-stage para as 2 APIs e o Worker, integrando Postgres, Redis e RabbitMQ | M5 | ORIGINAL_REQUEST §R7 |
| F22 | Suíte de Testes E2E Completos | Validação de 100% dos testes E2E opaque-box (Tiers 1 a 4) com execução com sucesso | M6 | E2E Testing Track |
| F23 | Endurecimento Adversarial | Testes adversariais de estresse, falha de infraestrutura e análise de cobertura de caminhos (Tier 5) | M6 | Final Milestone Phase 2 |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Fundação, Qualidade e Solution .NET 8 | F01, F02, F03, F04: Criação da Solution CashFlow.sln, 12 .csproj, .editorconfig, Directory.Build.props, .gitignore, git init e higienização de emojis | none | PLANNED |
| M2 | Serviço de Lançamentos (Write-Side) | F05, F06, F07, F08, F09, F10, F19: Endpoints de gravação, PostgreSQL, IdempotencyKey, RabbitMQ com Polly e testes unitários | M1 | PLANNED |
| M3 | Worker de Consolidação e DLQ | F11, F12, F13, F14, F15, F19: Consumo idempotente, agregação atômica no Postgres, Dead Letter Queue, cache Write-Through e testes | M2 | PLANNED |
| M4 | Serviço de Consolidado Diário (Read-Side) | F16, F17, F18, F19: Endpoints de leitura, Redis Cache (<5ms, >50 RPS), fallback resiliente no Postgres via Polly e testes | M3 | PLANNED |
| M5 | Docker Compose e Suíte de Testes Integrada | F20, F21, F19: Dockerfiles multi-stage, docker-compose.yml, init-db.sql, testes unitários/integrados consolidados | M4 | PLANNED |
| M6 | Marco Final: Aprovação 100% E2E e Endurecimento | F22, F23: Execução de 100% da suíte E2E (Tiers 1-4) e endurecimento adversarial (Tier 5) | M5, TEST_READY | PLANNED |

## Interface Contracts

### SharedKernel ↔ Todos os Serviços
- **IDomainEvent:**
  ```csharp
  public interface IDomainEvent
  {
      Guid EventId { get; }
      DateTime OccurredOn { get; }
  }
  ```
- **TransactionCreatedEvent:**
  ```csharp
  public record TransactionCreatedEvent(
      Guid TransactionId,
      string MerchantId,
      decimal Amount,
      string Type,
      string Description,
      DateTime CreatedAt
  ) : IDomainEvent;
  ```

### Write-Side (Transactions API)
- **POST /api/v1/transactions**
  - Headers: `X-Idempotency-Key` (opcional ou no corpo da requisição)
  - Request Body:
    ```json
    {
      "merchantId": "MERCH_001",
      "amount": 150.50,
      "type": "Credit",
      "description": "Venda no balcao",
      "idempotencyKey": "f3b4c1a2-..."
    }
    ```
  - Response (201 Created / 200 OK para idempotente repetido):
    ```json
    {
      "transactionId": "...",
      "merchantId": "MERCH_001",
      "amount": 150.50,
      "type": "Credit",
      "createdAt": "2026-09-05T01:25:00Z"
    }
    ```
  - Erro (400 Bad Request):
    ```json
    {
      "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
      "title": "Erro de validacao nos dados do lancamento",
      "status": 400,
      "detail": "O valor do lancamento deve ser estritamente maior que zero."
    }
    ```

### Read-Side (Consolidated API)
- **GET /api/v1/consolidated/{merchantId}/{date}**
  - Parâmetros de rota: `merchantId` (string, max 50), `date` (ISO 8601: `yyyy-MM-dd`)
  - Response (200 OK):
    ```json
    {
      "merchantId": "MERCH_001",
      "date": "2026-09-05",
      "totalCredits": 500.00,
      "totalDebits": 150.00,
      "closingBalance": 350.00,
      "transactionCount": 3,
      "lastUpdatedAt": "2026-09-05T01:25:00Z",
      "cached": true
    }
    ```
  - Comportamento sem movimentação: retorna 200 OK com saldos zerados (`closingBalance: 0.00`).

### Mensageria RabbitMQ
- Exchange: `cashflow.events` (Tipo: topic, durável)
- Routing Key: `transaction.transactioncreatedevent`
- Fila Principal: `cashflow.consolidated.transactions` (durável, x-dead-letter-exchange: `cashflow.events.dlx`, x-dead-letter-routing-key: `transactions.dlq`)
- Exchange DLX: `cashflow.events.dlx` (Tipo: direct, durável)
- Fila DLQ: `cashflow.consolidated.transactions.dlq` (durável, binding com `transactions.dlq`)

## Code Layout
```
E:\Sandbox\sdfghjytrews\
├── .editorconfig
├── Directory.Build.props
├── .gitignore
├── CashFlow.sln
├── docker-compose.yml
├── init-db.sql
├── README.md
├── docs/
│   └── adr/
│       ├── ADR-001-cqrs-event-driven.md
│       ├── ADR-002-caching-strategy.md
│       └── ADR-003-resilience-polly.md
├── src/
│   ├── Shared/
│   │   └── CashFlow.Shared.Domain/
│   │       ├── Events/
│   │       └── CashFlow.Shared.Domain.csproj
│   └── Services/
│       ├── Transactions/
│       │   ├── CashFlow.Transactions.Domain/
│       │   ├── CashFlow.Transactions.Application/
│       │   ├── CashFlow.Transactions.Infrastructure/
│       │   └── CashFlow.Transactions.Api/
│       └── Consolidated/
│           ├── CashFlow.Consolidated.Domain/
│           ├── CashFlow.Consolidated.Application/
│           ├── CashFlow.Consolidated.Infrastructure/
│           ├── CashFlow.Consolidated.Worker/
│           └── CashFlow.Consolidated.Api/
└── tests/
    ├── CashFlow.Transactions.UnitTests/
    ├── CashFlow.Consolidated.UnitTests/
    └── CashFlow.E2ETests/
```
