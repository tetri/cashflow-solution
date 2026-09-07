# ADR 006: Estratégia de Observabilidade, Telemetria e Diagnósticos Operacionais

## Status
**Aprovado**

## Contexto
Em arquiteturas de microsserviços distribuídas e orientadas a eventos (EDA) voltadas para fluxos financeiros de missão crítica, a visibilidade operacional é requisito fundamental para:
1. **Identificação de Incidentes e MTTR (Mean Time To Resolution):** Diagnosticar rapidamente degradações no broker RabbitMQ, cache Redis ou banco relacional PostgreSQL sem depender de depuração manual em servidores.
2. **Coleta de Métricas em Tempo Real:** Permitir a raspagem (scraping) de volumetria de transações, montantes financeiros acumulados, proporção de acertos de cache (*cache hit ratio*) e tempo de processamento contábil.
3. **Orquestração e Saúde dos Serviços:** Disponibilizar sondas padronizadas para que plataformas de contêineres (Kubernetes, AWS ECS, Docker Swarm) diferenciem quando um processo travou (Liveness) de quando suas dependências de rede estão temporariamente degradadas (Readiness).
4. **Extração Desacoplada de Logs:** Garantir que ferramentas de agregação de logs (FluentBit, Logstash, Vector, CloudWatch, Datadog) façam a extração de parâmetros de negócio sem depender de expressões regulares frágeis.

---

## Decisão
Adotamos o padrão dos **Três Pilares de Observabilidade** (Logs Estruturados, Health Checks Segregados e Métricas OpenMetrics/Prometheus), implementados de forma nativa e enxuta:

### 1. Logs Estruturados em NDJSON (Newline Delimited JSON)
- **Implementação:** Ativação do formatador nativo `AddJsonConsole` do .NET 8 nos três microsserviços (`Transactions API`, `Consolidated API` e `Consolidated Worker`).
- **Formato:** Cada entrada de log no `stdout` é emitida em linha única no formato JSON estrito, com timestamp ISO 8601 UTC (`yyyy-MM-ddTHH:mm:ss.fffZ`), nível de severidade e o nó estruturado `State`.
- **Extração Imediata:** Parâmetros como `{TransactionId}`, `{MerchantId}` e `{Amount}` são exportados como campos chave-valor de primeira classe, permitindo consultas e filtros instantâneos em dashboards sem sobrecarga de parse textual.

### 2. Sondas de Saúde Operacional Segregadas (Liveness vs Readiness)
- **Implementação:** Utilização do subsistema nativo `Microsoft.Extensions.Diagnostics.HealthChecks`.
- **Sonda de Vivacidade (`GET /health/live`):**
  - Executa apenas verificações leves de processo (Liveness).
  - Retorna HTTP `200 OK` para confirmar que a aplicação não está em deadlock.
- **Sonda de Prontidão (`GET /health/ready`):**
  - Testa ativamente as dependências de infraestrutura:
    - `Transactions API`: conectividade com PostgreSQL (`TransactionsDbContext.Database.CanConnectAsync`) e broker RabbitMQ (`IConnection.IsOpen`).
    - `Consolidated API`: conectividade com PostgreSQL (leitor) e cache Redis via comando de `PingAsync()`.
  - Retorna HTTP `200 OK` (ou `503 Service Unavailable` em falhas) com relatório JSON estruturado detalhando latência e status por componente.
- **Isolamento de Segurança:** As rotas de monitoramento são isentas da validação de chave de API (`X-Api-Key`), viabilizando checagens contínuas por sondas de rede.

### 3. Métricas de Infraestrutura e Negócio (Prometheus / OpenMetrics)
- **Implementação:** Biblioteca `prometheus-net.AspNetCore` (v8.2.1) e `prometheus-net`.
- **Métricas HTTP Automáticas:** Ativação de `app.UseHttpMetrics()`, gerando contadores de requisições (`http_requests_received_total`) e histogramas de latência (`http_request_duration_seconds`).
- **Métricas Customizadas de Domínio:**
  - `cashflow_transactions_created_total{type, status}`: Quantidade de lançamentos classificados por tipo (Credit/Debit) e resultado (success, duplicate, conflict, error).
  - `cashflow_transaction_amount_total{type}`: Volume financeiro acumulado.
  - `cashflow_consolidated_queries_total{cached}`: Consultas ao consolidado diário.
  - `cashflow_cache_hits_total` e `cashflow_cache_misses_total`: Eficiência do cache distribuído.
  - `cashflow_worker_events_processed_total{event_type, status}`: Eventos processados pelo Worker.
  - `cashflow_worker_event_duration_seconds`: Histograma de duração do processamento e conciliação contábil.
- **Endpoint de Scraping:** Exposição de `GET /metrics` nas APIs e servidor dedicado na porta `:9091/metrics` no Worker.

---

## Consequências

### Positivas
- **Diagnóstico Preciso:** Visibilidade completa do fluxo transacional desde o ingresso HTTP até a consolidação em memória e em disco.
- **Facilidade de Extração:** Eliminação de pipelines complexos de transformação de logs (Logstash filters / Regex parsers) devido ao NDJSON nativo.
- **Resiliência de Orquestração:** Prevenção de *restart loops* indevidos no Kubernetes através da separação entre `/health/live` e `/health/ready`.
- **Integração com Ferramental de Mercado:** Compatibilidade direta e sem atrito com Prometheus, Grafana, Datadog e Elasticsearch.

### Negativas / Mitigações
- **Consumo de Memória para Armazenamento de Séries Temporais de Métricas:** Métricas com alta cardinalidade podem elevar o uso de memória.
  - *Mitigação Adotada:* Rótulos estritamente restritos a valores enumerados de baixa cardinalidade (`Credit`, `Debit`, `success`, `duplicate`, `cached`), sem incluir chaves arbitrárias como IDs de usuário nos rótulos de métricas.
