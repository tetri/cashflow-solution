# Guia de Integração da API (Developer Experience)

Este documento é destinado aos desenvolvedores, equipes de front-end, aplicativos móveis e sistemas integradores que consomem os serviços da **CashFlow Platform**.

---

## 1. Visão Geral das APIs

A plataforma expõe duas APIs RESTful independentes com suporte a alta disponibilidade e idempotência estrita:

| Serviço | URL Base Local | Swagger / OpenAPI | Finalidade |
|---|---|---|---|
| **Transactions API** | `http://localhost:5001` | `http://localhost:5001/swagger` | Registro de entradas (créditos) e saídas (débitos) financeiros |
| **Consolidated API** | `http://localhost:5002` | `http://localhost:5002/swagger` | Consulta do relatório de saldo consolidado diário por comerciante |

---

## 2. Convenção de Idempotência no Lançamento de Transações

Para evitar cobranças ou lançamentos duplicados provocados por instabilidade de rede ou retentativas automáticas de aplicativos de checkout, a API de Lançamentos implementa o padrão de **Chave de Idempotência**.

### Como Utilizar:
Envie um identificador único universal (UUIDv4) no cabeçalho HTTP `X-Idempotency-Key` em cada requisição de lançamento:

```http
POST /api/v1/transactions HTTP/1.1
Host: localhost:5001
Content-Type: application/json
X-Idempotency-Key: e4d93f72-8854-4a7b-a3d1-9f20e4b86123

{
  "merchantId": "LOJA_CENTRO_01",
  "amount": 150.00,
  "type": "Credit",
  "description": "Venda no cartao de debito"
}
```

### Ciclo de Resposta da Idempotência:
* **Primeira requisição bem-sucedida:** Retorna `HTTP 201 Created` contendo os dados do lançamento persistido e o cabeçalho `Location`.
* **Requisição repetida com a mesma chave e os mesmos dados:** Retorna `HTTP 200 OK` devolvendo o registro existente sem criar duplicidade no caixa.
* **Requisição com chave idêntica mas com valores/dados divergentes:** Retorna `HTTP 409 Conflict` (ProblemDetails RFC 7231) alertando que a chave foi reutilizada indevidamente com outra intenção de negócio.

---

## 3. Endpoints Disponíveis

### 3.1 Registrar Lançamento Financeiro
* **Método:** `POST`
* **Rota:** `/api/v1/transactions`
* **Headers:**
  * `Content-Type: application/json`
  * `X-Idempotency-Key: <UUIDv4>` (Recomendado)

#### Corpo da Requisição (JSON):
```json
{
  "merchantId": "LOJA_CENTRO_01",
  "amount": 250.75,
  "type": "Credit",
  "description": "Venda de mercadorias no balcao"
}
```

*Campos:*
* `merchantId` (obrigatório, string, max 50): Identificador do estabelecimento.
* `amount` (obrigatório, decimal > 0.00): Valor monetário positivo.
* `type` (obrigatório, string): Natureza contábil (`Credit` para entradas, `Debit` para saídas).
* `description` (obrigatório, string, max 255): Motivo do lançamento (proibido conter emojis).

#### Exemplo de Resposta de Sucesso (HTTP 201 Created):
```json
{
  "transactionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "merchantId": "LOJA_CENTRO_01",
  "amount": 250.75,
  "type": "Credit",
  "createdAt": "2026-09-05T12:30:00Z"
}
```

---

### 3.2 Consultar Lançamento por Identificador
* **Método:** `GET`
* **Rota:** `/api/v1/transactions/{id}`
* **Exemplo de Resposta (HTTP 200 OK):**
```json
{
  "transactionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "merchantId": "LOJA_CENTRO_01",
  "amount": 250.75,
  "type": "Credit",
  "description": "Venda de mercadorias no balcao",
  "createdAt": "2026-09-05T12:30:00Z"
}
```

---

### 3.3 Consultar Saldo Diário Consolidado
* **Método:** `GET`
* **Rota:** `/api/v1/consolidated/{merchantId}/{date}`
* **Parâmetros de Rota:**
  * `merchantId`: Código do comerciante (ex: `LOJA_CENTRO_01`).
  * `date`: Data contábil no padrão ISO 8601 (`yyyy-MM-dd`, ex: `2026-09-05`).

#### Exemplo de Resposta de Sucesso (HTTP 200 OK):
```json
{
  "merchantId": "LOJA_CENTRO_01",
  "date": "2026-09-05",
  "totalCredits": 1250.50,
  "totalDebits": 300.00,
  "closingBalance": 950.50,
  "transactionCount": 8,
  "lastUpdatedAt": "2026-09-05T12:30:05Z",
  "cached": true
}
```

*Nota sobre desempenho:* A propriedade `"cached": true` indica que a consulta foi servida em menos de 5 milissegundos a partir da memória RAM do Redis. Em caso de dias sem movimentação registrada, a API retorna `HTTP 200 OK` com saldos zerados (`closingBalance: 0.00`).

### 3.4 Endpoints de Observabilidade e Diagnósticos Operacionais

Ambas as APIs expõem rotas públicas de monitoramento (isentas de autenticação via `X-Api-Key`), viabilizando sondagens automatizadas por orquestradores de contêineres e coletores de métricas:

| Endpoint | Método | Finalidade | Formato de Retorno |
|---|---|---|---|
| `/health/live` | `GET` | **Sonda de Vivacidade (Liveness):** Confirma que o runtime da aplicação está ativo e sem deadlocks. | JSON simples (`{ "status": "Saudavel", ... }`) |
| `/health/ready` | `GET` | **Sonda de Prontidão (Readiness):** Testa ativamente as conexões com o PostgreSQL, Redis e RabbitMQ. | JSON detalhado com latência e status por componente |
| `/health` | `GET` | **Status Geral:** Verificação abrangente de saúde operacional para ferramentas legadas. | JSON detalhado com latência e status por componente |
| `/metrics` | `GET` | **Métricas Prometheus:** Exporta métricas HTTP e contadores de negócio no padrão OpenMetrics. | Texto canônico Prometheus para scraping |

---

## 4. Tratamento de Erros e Padrão ProblemDetails (RFC 7231 / RFC 7807)

Todas as respostas de erro da plataforma seguem estritamente o padrão **RFC 7807 (ProblemDetails)** e as recomendações **OWASP / CWE-209**, fornecendo códigos de erro estáveis no campo `errorCode` e carimbo de data/hora UTC no campo `timestamp`, sem vazamento de stack traces internos.

### Exemplo de Erro de Validação (HTTP 400 Bad Request):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Erro de validacao nos dados do lancamento",
  "status": 400,
  "detail": "O valor do lancamento deve ser estritamente maior que zero.",
  "errorCode": "CF_TX_003",
  "timestamp": "2026-09-07T20:30:00.123Z"
}
```

### Exemplo de Conflito de Idempotência (HTTP 409 Conflict):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "Conflito de idempotencia na transacao",
  "status": 409,
  "detail": "A chave de idempotencia informada ja foi utilizada previamente com valores ou parametros divergentes.",
  "errorCode": "CF_TX_006",
  "timestamp": "2026-09-07T20:30:05.456Z"
}
```

### Exemplo de Falha de Autenticação (HTTP 401 Unauthorized):
```json
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Acesso nao autorizado",
  "status": 401,
  "detail": "Credenciais de autenticacao ausentes ou invalidas. Forneca o cabecalho 'X-Api-Key' ou 'Authorization: Bearer <token>' valido.",
  "errorCode": "CF_AUTH_001",
  "timestamp": "2026-09-07T20:30:10.789Z"
}
```

---

## 5. Coleção de Exemplos Práticos com cURL

```bash
# 1. Registrar um Credito de R$ 500,00 (Autenticado via X-Api-Key)
curl -X POST http://localhost:5001/api/v1/transactions \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: cashflow-secret-api-key-2026" \
  -H "X-Idempotency-Key: a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d" \
  -d '{"merchantId":"LOJA_CENTRO_01","amount":500.00,"type":"Credit","description":"Recebimento de vendas"}'

# 2. Registrar um Debito de R$ 120,00 (Autenticado via Bearer Token)
curl -X POST http://localhost:5001/api/v1/transactions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer cashflow-secret-api-key-2026" \
  -H "X-Idempotency-Key: f9e8d7c6-b5a4-3210-fedc-ba9876543210" \
  -d '{"merchantId":"LOJA_CENTRO_01","amount":120.00,"type":"Debit","description":"Pagamento de frete"}'

# 3. Consultar o Consolidado do Dia (Resposta sub-5ms via Redis)
curl -X GET http://localhost:5002/api/v1/consolidated/LOJA_CENTRO_01/2026-09-05 \
  -H "Accept: application/json" \
  -H "X-Api-Key: cashflow-secret-api-key-2026"

# 4. Verificar a Sonda de Vivacidade (Liveness Probe - Sem autenticacao)
curl -X GET http://localhost:5001/health/live

# 5. Verificar a Sonda de Prontidão (Readiness Probe com status de banco e mensageria)
curl -X GET http://localhost:5001/health/ready

# 6. Coletar Metricas Operacionais para o Prometheus
curl -X GET http://localhost:5001/metrics
```
