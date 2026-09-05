# CashFlow Solution - Controle de Fluxo de Caixa

Arquitetura de microsservicos de alta disponibilidade e resiliencia desenvolvida em **.NET 8**, **Clean Architecture**, **CQRS**, **Event-Driven Architecture (RabbitMQ)**, **PostgreSQL** e **Redis**.

---

## 1. Arquitetura e Decisoes de Design

A solucao resolve o problema de isolamento e alta disponibilidade separando o fluxo de gravacao de lancamentos do fluxo de leitura do consolidado diario:

1. **Servico de Lancamentos (Write Service):**
   - Recebe debitos e creditos com latencia ultrabaixa.
   - Grava a transacao no PostgreSQL e publica o evento `TransactionCreatedEvent` no broker RabbitMQ com resiliencia via **Polly** (Retry + Circuit Breaker).
   - Nao depende da disponibilidade do servico de consolidado para operar.

2. **Consolidated Worker (Background Processor):**
   - Consome os eventos do RabbitMQ de forma idempotente.
   - Atualiza o registro agregado diario no PostgreSQL e sincroniza o cache Redis.

3. **Servico de Consolidado Diario (Read Service):**
   - Atende picos superiores a **50 requisicoes por segundo** via cache **Redis** em memoria (<5ms).
   - Possui fallback automatico para o PostgreSQL caso o Redis esteja indisponivel.

Consulte os registros de decisao arquitetural detalhados:
- [ADR 001 - Padrao CQRS e Mensageria](docs/adr/ADR-001-cqrs-event-driven.md)
- [ADR 002 - Estrategia de Cache Distribuido](docs/adr/ADR-002-caching-strategy.md)
- [ADR 003 - Tolerancia a Falhas e Resiliencia com Polly](docs/adr/ADR-003-resilience-polly.md)

---

## 2. Como Executar a Aplicacao

### Pre-requisitos
- [Docker](https://www.docker.com/) e Docker Compose instalados.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (opcional, apenas para desenvolvimento local).

### Passo a passo

1. **Clone o repositorio e inicie os containers:**
```bash
docker-compose up --build -d
```

2. **Verifique se os servicos estao saudaveis:**
```bash
docker-compose ps
```

3. **Portas e Servicos Expostos:**
- **Transactions API:** `http://localhost:5001` (Swagger: `http://localhost:5001/swagger`)
- **Consolidated API:** `http://localhost:5002` (Swagger: `http://localhost:5002/swagger`)
- **RabbitMQ Management UI:** `http://localhost:15672` (Usuario: `guest`, Senha: `guest`)
- **PostgreSQL:** `localhost:5432` (Base de Dados: `cashflow_db`, Usuario: `postgres`, Senha: `postgrespassword`)
- **Redis:** `localhost:6379`

---

## 3. Execucao dos Testes

Para executar os testes unitarios e de integracao utilizando xUnit e NSubstitute:

```bash
# Executar todos os testes da solucao
dotnet test --logger "console;verbosity=detailed"

# Executar testes com relatorio de cobertura
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

---

## 4. Exemplos de Chamadas de API

### 4.1 Registrar um Credito (Lancamento)
```bash
curl -X POST http://localhost:5001/api/v1/transactions \
  -H "Content-Type: application/json" \
  -d '{
    "merchantId": "MERCHANT_001",
    "amount": 250.75,
    "type": "Credit",
    "description": "Venda de produtos de padaria"
  }'
```

### 4.2 Registrar um Debito (Lancamento)
```bash
curl -X POST http://localhost:5001/api/v1/transactions \
  -H "Content-Type: application/json" \
  -d '{
    "merchantId": "MERCHANT_001",
    "amount": 50.00,
    "type": "Debit",
    "description": "Pagamento de fornecedor"
  }'
```

### 4.3 Consultar o Consolidado Diario (Atende >50 req/s via Redis)
```bash
curl -X GET "http://localhost:5002/api/v1/consolidated/MERCHANT_001/2026-09-04" \
  -H "Accept: application/json"
```

**Resposta esperada:**
```json
{
  "merchantId": "MERCHANT_001",
  "date": "2026-09-04",
  "totalCredits": 250.75,
  "totalDebits": 50.00,
  "closingBalance": 200.75,
  "transactionCount": 2,
  "lastUpdatedAt": "2026-09-04T22:10:00Z",
  "cached": true
}
```

---

## 5. Evolucoes Futuras da Arquitetura

### 5.1 Change Data Capture (CDC) com Debezium & Apache Kafka
- **Objetivo:** Eliminar a necessidade de o servico de lancamentos publicar eventos de forma sincrona/dupla transacao (*Dual-Write Problem*).
- **Implementacao:**
  1. O servico de lancamentos grava a transacao na tabela de `transactions` e insere um registro na tabela `outbox_events` em uma **unica transacao atomica** no PostgreSQL.
  2. O conector **Debezium for PostgreSQL** le o WAL (*Write-Ahead Log*) do banco e transmite os eventos com semantica *exactly-once* para topicos particionados no **Apache Kafka** (chaveado por `merchant_id`).
  3. Isso garante **zero perda de dados**, ordenacao estrita por comerciante e desacoplamento total da camada de aplicacao em relacao ao broker de mensagens.

### 5.2 Orquestracao com Kubernetes (K8s) & Autoscaling (KEDA)
- **Deployment & Isolamento:** Separacao dos microsservicos em Pods com replicas independentes e *Resource Quotas* bem definidos.
- **KEDA (Kubernetes Event-driven Autoscaling):**
  - O `Consolidated Worker` escalara horizontalmente de 1 para ate 50 replicas com base no tamanho da fila no RabbitMQ/Kafka (*queue lag*).
  - A `Consolidated API` escalara com **HPA (Horizontal Pod Autoscaler)** baseado em metricas customizadas de CPU, latencia e RPS (mantendo < 5ms).

### 5.3 Stack de Observabilidade com Elastic Stack (ELK) e OpenTelemetry
- **OpenTelemetry SDK (.NET 8):** Instrumentacao de traces distribuidos (W3C TraceContext) injetados nos headers das mensagens AMQP/Kafka.
- **Elasticsearch + Logstash / OpenTelemetry Collector:**
  - Armazenamento centralizado de logs estruturados em JSON com campos `traceId`, `spanId`, `merchantId` e `environment`.
- **Kibana & Elastic APM:**
  - Visualizacao de mapa de dependencias distribuido, taxas de erro por servico, latencia de p95/p99 e dashboards em tempo real de liquidez e volume de lancamentos.
- **Prometheus & Grafana:** Coleta de metricas do Polly (taxas de acionamento de Circuit Breaker, Retries e Fallbacks).
