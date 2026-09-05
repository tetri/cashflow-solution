# Guia de Integracao da API (Developer Experience)

Este documento e destinado aos desenvolvedores, equipes de front-end, aplicativos moveis e sistemas integradores que consomem os servicos da **CashFlow Platform**.

---

## 1. Visao Geral das APIs

A plataforma expoe duas APIs RESTful independentes com suporte a alta disponibilidade e idempotencia estrita:

| Servico | URL Base Local | Swagger / OpenAPI | Finalidade |
|---|---|---|---|
| **Transactions API** | `http://localhost:5001` | `http://localhost:5001/swagger` | Registro de entradas (creditos) e saidas (debitos) financeiros |
| **Consolidated API** | `http://localhost:5002` | `http://localhost:5002/swagger` | Consulta do relatorio de saldo consolidado diario por comerciante |

---

## 2. Convencao de Idempotencia no Lancamento de Transacoes

Para evitar cobrancas ou lancamentos duplicados provocados por instabilidade de rede ou retentativas automaticas de aplicativos de checkout, a API de Lancamentos implementa o padrao de **Chave de Idempotencia**.

### Como Utilizar:
Envie um identificador unico universal (UUIDv4) no cabecalho HTTP `X-Idempotency-Key` em cada requisicao de lancamento:

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

### Ciclo de Resposta da Idempotencia:
* **Primeira requisicao bem-sucedida:** Retorna `HTTP 201 Created` contendo os dados do lancamento persistido e o cabecalho `Location`.
* **Requisicao repetida com a mesma chave e os mesmos dados:** Retorna `HTTP 200 OK` devolvendo o registro existente sem criar duplicidade no caixa.
* **Requisicao com chave identica mas com valores/dados divergentes:** Retorna `HTTP 409 Conflict` (ProblemDetails RFC 7231) alertando que a chave foi reutilizada indevidamente com outra intencao de negocio.

---

## 3. Endpoints Disponiveis

### 3.1 Registrar Lancamento Financeiro
* **Metodo:** `POST`
* **Rota:** `/api/v1/transactions`
* **Headers:**
  * `Content-Type: application/json`
  * `X-Idempotency-Key: <UUIDv4>` (Recomendado)

#### Corpo da Requisicao (JSON):
```json
{
  "merchantId": "LOJA_CENTRO_01",
  "amount": 250.75,
  "type": "Credit",
  "description": "Venda de mercadorias no balcao"
}
```

*Campos:*
* `merchantId` (obrigatorio, string, max 50): Identificador do estabelecimento.
* `amount` (obrigatorio, decimal > 0.00): Valor monetario positivo.
* `type` (obrigatorio, string): Natureza contabil (`Credit` para entradas, `Debit` para saidas).
* `description` (obrigatorio, string, max 255): Motivo do lancamento (proibido conter emojis).

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

### 3.2 Consultar Lancamento por Identificador
* **Metodo:** `GET`
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

### 3.3 Consultar Saldo Diario Consolidado
* **Metodo:** `GET`
* **Rota:** `/api/v1/consolidated/{merchantId}/{date}`
* **Parametros de Rota:**
  * `merchantId`: Codigo do comerciante (ex: `LOJA_CENTRO_01`).
  * `date`: Data contábil no padrao ISO 8601 (`yyyy-MM-dd`, ex: `2026-09-05`).

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

*Nota sobre desempenho:* A propriedade `"cached": true` indica que a consulta foi servida em menos de 5 milissegundos a partir da memoria RAM do Redis. Em caso de dias sem movimentacao registrada, a API retorna `HTTP 200 OK` com saldos zerados (`closingBalance: 0.00`).

---

## 4. Tratamento de Erros e Padrao ProblemDetails (RFC 7231)

Todas as respostas de erro da plataforma seguem estritamente o padrao **RFC 7231 / RFC 7807 (ProblemDetails)**, com titulos e detalhes redigidos em portugues culto.

### Exemplo de Erro de Validacao (HTTP 400 Bad Request):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Erro de validacao nos dados do lancamento",
  "status": 400,
  "detail": "O valor do lancamento deve ser estritamente maior que zero."
}
```

### Exemplo de Conflito de Idempotencia (HTTP 409 Conflict):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "Conflito de idempotencia na transacao",
  "status": 409,
  "detail": "A chave de idempotencia informada ja foi utilizada previamente com valores ou parametros divergentes."
}
```

---

## 5. Colecao de Exemplos Praticos com cURL

```bash
# 1. Registrar um Credito de R$ 500,00
curl -X POST http://localhost:5001/api/v1/transactions \
  -H "Content-Type: application/json" \
  -H "X-Idempotency-Key: a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d" \
  -d '{"merchantId":"LOJA_CENTRO_01","amount":500.00,"type":"Credit","description":"Recebimento de vendas"}'

# 2. Registrar um Debito de R$ 120,00
curl -X POST http://localhost:5001/api/v1/transactions \
  -H "Content-Type: application/json" \
  -H "X-Idempotency-Key: f9e8d7c6-b5a4-3210-fedc-ba9876543210" \
  -d '{"merchantId":"LOJA_CENTRO_01","amount":120.00,"type":"Debit","description":"Pagamento de frete"}'

# 3. Consultar o Consolidado do Dia
curl -X GET http://localhost:5002/api/v1/consolidated/LOJA_CENTRO_01/2026-09-05 \
  -H "Accept: application/json"
```
