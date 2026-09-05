# Arquitetura de Seguranca e Modelo de Ameacas (Threat Modeling)

Este documento descreve a estrategia de seguranca aplicada a solucao **CashFlow Platform**, atendendo as diretrizes de integridade, confiabilidade e protecao de dados estipuladas no desafio do arquiteto.

---

## 1. Modelo de Ameacas STRIDE

Aplicamos o modelo STRIDE (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege) para identificar vulnerabilidades potenciais e as contramedidas implementadas:

| Categoria STRIDE | Ameaca Identificada | Contramedida Implementada na Solucao |
|---|---|---|
| **Spoofing (Falsificacao)** | Requisicao forjada passando-se por outro comerciante | No escopo de evolucao, adocao de tokens JWT assinados via OAuth2/OIDC com claim `merchant_id` validada no pipeline ASP.NET Core. |
| **Tampering (Adulteracao)** | Alteracao indevida de dados contabeis ou duplicacao de lancamentos | Imutabilidade dos registros de lancamento (Append-Only) e garantia de Idempotencia estrita via `X-Idempotency-Key` com indice unico no PostgreSQL. |
| **Repudiation (Repudio)** | Comerciante negar que realizou determinado lancamento | Log transacional auditavel com `id`, `merchant_id`, `created_at` (UTC imutavel) e persistencia em tabela relacional ACID. |
| **Information Disclosure (Vazamento)** | Exposicao de detalhes de infraestrutura ou dados sensiveis em falhas | Respostas padronizadas via RFC 7231 ProblemDetails em portugues culto sem stack trace; supressao de comandos SQL em logs de producao. |
| **Denial of Service (DoS)** | Saturacao do servico de consolidado por picos de requisicao ou Cache Stampede | Cache distribuido Redis (<5ms), protecao com SemaphoreSlim (Double-Checked Locking anti-stampede) e Circuit Breaker via Polly v8. |
| **Elevation of Privilege (Elevacao)** | Acesso nao autorizado a funcoes administrativas de caixa | Segregacao de servicos em microsservicos independentes e execucao dos containers Docker sob usuario nao-root (`appuser`). |

---

## 2. Seguranca em Camadas (Defense in Depth)

### 2.1 Criptografia e Protecao de Dados em Transito e Repouso
- **Em Transito:** Comunicacao externa estritamente sob TLS 1.3 com politicas HSTS.
- **Service-to-Service:** Comunicacao entre microsservicos e brokers isolada em rede privada virtual bridge/Docker Network (sem exposicao de portas de dados para a internet publica).
- **Em Repouso:** PostgreSQL configurado com suporte a TDE (Transparent Data Encryption) ou criptografia de volume no storage subjacente (LUKS / AWS EBS Encryption).

### 2.2 Sanitizacao de Entradas e Validacao Rigorosa
- Inspecao ativa de todos os payloads e parametros de rota via utilitario `EmojiDetector` e regex compilada.
- Bloqueio sumario de caracteres malformados, emojis ou tentativas de injecao com retorno padronizado HTTP 400 Bad Request.

### 2.3 Seguranca de Containers
- Imagens Docker multi-stage baseadas em Alpine Linux minimizado, reduzindo expressivamente a superficie de ataque e vulnerabilidades de pacotes (CVEs).
- Execucao expressa sob usuario sem privilegios de superusuario (`USER appuser`).
- Variaveis de ambiente contendo credenciais nunca versionadas em codigo fonte direto, sendo injetadas em tempo de execucao via segredos de orquestracao (Docker Secrets / Kubernetes Secrets).
