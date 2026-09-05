# Arquitetura de Segurança e Modelo de Ameaças (Threat Modeling)

Este documento descreve a estratégia de segurança aplicada à solução **CashFlow Platform**, atendendo às diretrizes de integridade, confiabilidade e proteção de dados estipuladas no desafio do arquiteto.

---

## 1. Modelo de Ameaças STRIDE

Aplicamos o modelo STRIDE (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege) para identificar vulnerabilidades potenciais e as contramedidas implementadas:

| Categoria STRIDE | Ameaça Identificada | Contramedida Implementada na Solução |
|---|---|---|
| **Spoofing (Falsificação)** | Requisição forjada passando-se por outro comerciante | No escopo de evolução, adoção de tokens JWT assinados via OAuth2/OIDC com claim `merchant_id` validada no pipeline ASP.NET Core. |
| **Tampering (Adulteração)** | Alteração indevida de dados contábeis ou duplicação de lançamentos | Imutabilidade dos registros de lançamento (Append-Only) e garantia de Idempotência estrita via `X-Idempotency-Key` com índice único no PostgreSQL. |
| **Repudiation (Repúdio)** | Comerciante negar que realizou determinado lançamento | Log transacional auditável com `id`, `merchant_id`, `created_at` (UTC imutável) e persistência em tabela relacional ACID. |
| **Information Disclosure (Vazamento)** | Exposição de detalhes de infraestrutura ou dados sensíveis em falhas | Respostas padronizadas via RFC 7231 ProblemDetails em português culto sem stack trace; supressão de comandos SQL em logs de produção. |
| **Denial of Service (DoS)** | Saturação do serviço de consolidado por picos de requisição ou Cache Stampede | Cache distribuído Redis (<5ms), proteção com SemaphoreSlim (Double-Checked Locking anti-stampede) e Circuit Breaker via Polly v8. |
| **Elevation of Privilege (Elevação)** | Acesso não autorizado a funções administrativas de caixa | Segregação de serviços em microsserviços independentes e execução dos containers Docker sob usuário não-root (`appuser`). |

---

## 2. Segurança em Camadas (Defense in Depth)

### 2.1 Criptografia e Proteção de Dados em Trânsito e Repouso
- **Em Trânsito:** Comunicação externa estritamente sob TLS 1.3 com políticas HSTS.
- **Service-to-Service:** Comunicação entre microsserviços e brokers isolada em rede privada virtual bridge/Docker Network (sem exposição de portas de dados para a internet pública).
- **Em Repouso:** PostgreSQL configurado com suporte a TDE (Transparent Data Encryption) ou criptografia de volume no storage subjacente (LUKS / AWS EBS Encryption).

### 2.2 Sanitização de Entradas e Validação Rigorosa
- Inspeção ativa de todos os payloads e parâmetros de rota via utilitário `EmojiDetector` e regex compilada.
- Bloqueio sumário de caracteres malformados, emojis ou tentativas de injeção com retorno padronizado HTTP 400 Bad Request.

### 2.3 Segurança de Containers
- Imagens Docker multi-stage baseadas em Alpine Linux minimizado, reduzindo expressivamente a superfície de ataque e vulnerabilidades de pacotes (CVEs).
- Execução expressa sob usuário sem privilégios de superusuário (`USER appuser`).
- Variáveis de ambiente contendo credenciais nunca versionadas em código-fonte direto, sendo injetadas em tempo de execução via segredos de orquestração (Docker Secrets / Kubernetes Secrets).
