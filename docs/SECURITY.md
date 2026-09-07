# Arquitetura de Segurança e Modelo de Ameaças (Threat Modeling)

Este documento descreve a estratégia de segurança aplicada à solução **CashFlow Platform**, atendendo às diretrizes do **OWASP API Security Top 10**, integridade, confiabilidade e proteção de dados estipuladas no desafio do arquiteto.

---

## 1. Modelo de Ameaças STRIDE e Conformidade OWASP

Aplicamos o modelo STRIDE (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege) para identificar vulnerabilidades potenciais e as contramedidas ativas implementadas:

| Categoria STRIDE | Ameaça Identificada | Contramedida Implementada na Solução (OWASP) |
|---|---|---|
| **Spoofing (Falsificação)** | Requisição forjada ou anônima passando-se por outro comerciante | **OWASP API1 & API2:** Middleware de autenticação obrigatório via cabeçalho `X-Api-Key` ou `Authorization: Bearer <token>`, rejeitando chamadas anônimas com HTTP 401 Unauthorized (`CF_AUTH_001`). |
| **Tampering (Adulteração)** | Alteração indevida de dados contábeis ou duplicação de lançamentos | Imutabilidade dos registros de lançamento (Append-Only) e garantia de Idempotência estrita via `X-Idempotency-Key` com índice único no PostgreSQL. |
| **Repudiation (Repúdio)** | Comerciante negar que realizou determinado lançamento | Log transacional auditável com `id`, `merchant_id`, `created_at` (UTC imutável) e persistência em tabela relacional ACID. |
| **Information Disclosure (Vazamento)** | Exposição de detalhes de infraestrutura ou dados sensíveis em falhas | **CWE-209 / RFC 7807:** Respostas padronizadas ProblemDetails com códigos de erro únicos legíveis por máquina (`CashFlowErrorCodes`), sem vazamento de stack trace ou dados internos. |
| **Denial of Service (DoS)** | Saturação do serviço de consolidado por picos de requisição ou Cache Stampede | **OWASP API4:** Cache distribuído Redis (<5ms), proteção com SemaphoreSlim (Double-Checked Locking anti-stampede) e Circuit Breaker via Polly v8. |
| **Elevation of Privilege (Elevação)** | Acesso não autorizado a funções administrativas de caixa | Segregação de serviços em microsserviços independentes e execução dos containers Docker sob usuário não-root (`appuser`). |

---

## 2. Segurança em Camadas (Defense in Depth)

### 2.1 Autenticação e Autorização de APIs (OWASP API1 & API2)
- **Cabeçalho Seguro:** Endpoints operacionais (`/api/v1/transactions` e `/api/v1/consolidated`) exigem credencial válida via `X-Api-Key` ou `Authorization: Bearer <token>`.
- **Parametrização Segura:** A chave esperada é lida das variáveis de ambiente (`API_KEY` ou `Authentication__ApiKey`), com modelo disponibilizado em `.env.example`.
- **Isolamento de Endpoints Públicos:** Apenas as rotas de monitoramento (`/health`) e documentação (`/swagger`) são públicas.

### 2.2 Cabeçalhos de Segurança HTTP (OWASP Secure Headers)
Ambas as APIs injetam cabeçalhos defensivos nativamente em todas as respostas HTTP:
- `X-Content-Type-Options: nosniff` (prevenção contra MIME sniffing)
- `X-Frame-Options: DENY` (prevenção contra Clickjacking)
- `Content-Security-Policy: default-src 'self'` (mitigação de injeção de scripts e XSS)
- `Referrer-Policy: no-referrer` (proteção de metadados de navegação)
- `X-Permitted-Cross-Domain-Policies: none` (bloqueio de políticas de domínio cruzado legadas)

### 2.3 Tratamento de Erros Estruturados e Códigos Únicos (RFC 7807 / CWE-209)
Em conformidade com a RFC 7807 e as recomendações do OWASP para respostas seguras de API, as falhas expõem códigos estáveis no nó `errorCode`:

| Código de Erro | Significado Operacional | Status HTTP |
|---|---|---|
| `CF_AUTH_001` | Credenciais de autenticação ausentes ou inválidas | 401 Unauthorized |
| `CF_AUTH_002` | Acesso negado ao recurso solicitado | 403 Forbidden |
| `CF_TX_001` | Parâmetros inválidos ou caracteres proibidos no lançamento | 400 Bad Request |
| `CF_TX_002` | Identificador do comerciante inválido ou ausente | 400 Bad Request |
| `CF_TX_003` | Valor da transação menor ou igual a zero | 400 Bad Request |
| `CF_TX_004` | Descrição do lançamento ausente ou excessiva | 400 Bad Request |
| `CF_TX_005` | Tipo de transação inválido (diferente de Credit ou Debit) | 400 Bad Request |
| `CF_TX_006` | Conflito de idempotência (reutilização com dados divergentes) | 409 Conflict |
| `CF_TX_007` | Transação financeira não localizada | 404 Not Found |
| `CF_CONS_001` | Parâmetros de consulta do consolidado inválidos | 400 Bad Request |
| `CF_CONS_002` | Formato de data inválido (esperado: yyyy-MM-dd) | 400 Bad Request |
| `CF_CONS_003` | Consolidado diário não encontrado para a data informada | 404 Not Found |
| `CF_SYS_001` | Erro interno não tratado (capturado sem vazamento de stack) | 500 Internal Server Error |

### 2.4 Criptografia e Proteção de Dados em Trânsito e Repouso
- **Em Trânsito:** Comunicação externa sob TLS 1.3 com políticas HSTS.
- **Service-to-Service:** Comunicação interna entre microsserviços e brokers isolada em rede privada virtual bridge (`cashflow-network`), sem exposição das portas de banco de dados e mensageria para a internet pública.
- **Em Repouso:** PostgreSQL com suporte a criptografia de volume no storage subjacente (LUKS / AWS EBS Encryption).

### 2.5 Sanitização de Entradas e Validação Rigorosa
- Inspeção ativa de todos os payloads e parâmetros de rota via utilitário `EmojiDetector` e regex compilada.
- Bloqueio sumário de caracteres malformados, emojis ou tentativas de injeção com retorno padronizado HTTP 400 Bad Request.

### 2.6 Segurança de Containers
- Imagens Docker multi-stage baseadas em Alpine Linux minimizado, reduzindo expressivamente a superfície de ataque e vulnerabilidades de pacotes (CVEs).
- Execução expressa sob usuário sem privilégios de superusuário (`USER appuser`).
- Credenciais parametrizadas via variáveis de ambiente (`.env` e `.env.example`), sem valores sensíveis comitados no repositório.
