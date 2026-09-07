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
- **Isolamento de Endpoints Públicos:** Apenas as rotas de observabilidade (`/health`, `/health/live`, `/health/ready`, `/metrics`) e documentação interativa (`/swagger`) são públicas, viabilizando sondagens operacionais por orquestradores (Kubernetes/ECS) e scraping de métricas pelo Prometheus sem expor dados sensíveis ou permitir mutações de negócio.

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

### 2.7 Segregação de Privilégios no Banco de Dados (Princípio do Menor Privilégio - PoLP)
A separação arquitetural CQRS (Command Query Responsibility Segregation) reflete-se diretamente na camada de persistência através de papéis (roles) dedicados no PostgreSQL, eliminando o risco de um único usuário administrativo compartilhado:

- **Usuário de Escrita (`cashflow_writer`):**
  - Utilizado pelos serviços `transactions-api` e `consolidated-worker`.
  - Permissões concedidas: `SELECT, INSERT, UPDATE` exclusivamente sobre as tabelas operacionais (`transactions`, `daily_consolidated`, `processed_events`).
  - Sem privilégios de superusuário (`SUPERUSER`), criação de bancos (`CREATEDB`) ou alteração de esquemas (DDL).
- **Usuário de Leitura (`cashflow_reader`):**
  - Utilizado exclusivamente pelo serviço `consolidated-api`.
  - Permissões concedidas: estritamente `SELECT` na tabela `daily_consolidated`.
  - Revogações explícitas: qualquer operação de escrita (`INSERT, UPDATE, DELETE, TRUNCATE`) é sumariamente bloqueada pelo SGBD.
  - Bloqueio de tabelas sensíveis: o usuário de leitura não possui permissão para consultar a tabela de transações brutas (`transactions`) nem o log de eventos (`processed_events`).
- **Defesa em Profundidade contra SQL Injection e Vazamento:**
  Mesmo na remota hipótese de uma falha ou injeção de consulta no endpoint de leitura, um invasor jamais conseguirá extrair dados de transações individuais ou adulterar saldos consolidados, pois o PostgreSQL bloqueia a operação em nível de permissão de catálogo (`permission denied for table transactions`).
- **Parametrização Segura:**
  Todos os usuários e senhas (`POSTGRES_USER`, `POSTGRES_PASSWORD`, `DB_WRITER_USER`, `DB_WRITER_PASSWORD`, `DB_READER_USER`, `DB_READER_PASSWORD`) são gerenciados via arquivo `.env`, desacoplados do repositório de código e provisionados deterministicamente no script `init-db.sql`.

---

## 3. Engenharia Aumentada por IA com Governança Crítica Humana

A construção da plataforma CashFlow combinou aceleração de produtividade com inteligência artificial e rigorosa governança arquitetural humana:

1. **Aceleração via Agentes Autônomos:**
   O scaffolding estrutural, contratos de dados, implementação de padrões (Clean Architecture, CQRS, Repository) e suítes de testes unitários e de integração foram gerados e iterados com auxílio de agentes de IA.
2. **O Papel Crítico da Governança Arquitetural Sênior:**
   Modelos de linguagem e agentes generativos tendem a entregar soluções funcionalmente corretas, mas que com frequência omitem requisitos não-funcionais profundos de segurança corporativa e conformidade financeira:
   - **Vulnerabilidades Omitidas por Padrão pela IA:** APIs sem autenticação nativa, respostas de erro vazando mensagens de exceção em texto livre em vez de códigos de erro canônicos RFC 7807/OWASP, cabeçalhos de segurança permissivos ou faltantes (CSP, HSTS) e uso simplista do usuário `postgres` com permissões totais para todas as aplicações.
   - **Intervenção Humana Especializada:** A análise crítica do arquiteto identificou tais lacunas de segurança operacional, exigindo o endurecimento da infraestrutura: inclusão do middleware de autenticação (`X-Api-Key`), implementação de códigos estáveis (`CashFlowErrorCodes`), proteção rigorosa contra injeção e XSS, e segregação completa de usuários de banco de dados (`cashflow_writer` vs `cashflow_reader`) guiada pelo Princípio do Menor Privilégio.
3. **Lição de Engenharia para Defesa Técnica:**
   Ferramentas de IA são aceleradores de produtividade de alto impacto, mas a responsabilidade sobre segurança, integridade transacional, desenho de defesa em profundidade e conformidade regulatória permanece integralmente como competência do engenheiro/arquiteto humano sênior.

