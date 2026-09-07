# ADR 005: Seleção do SGBD Relacional PostgreSQL 16 (LTS)

## Status
**Aprovado**

## Contexto
O sistema de fluxo de caixa exige persistência relacional com conformidade estrita às propriedades **ACID** (Atomicidade, Consistência, Isolamento e Durabilidade) para garantir a integridade financeira dos lançamentos e do saldo consolidado diário.

Os requisitos centrais para a escolha do banco de dados relacional e sua versão específica incluem:
1. **Garantia de Não Repúdio e Idempotência:** Capacidade de impor unicidade com índices compostos únicos (`merchant_id`, `idempotency_key`) sem gerar gargalos de contenção ou lock em tabelas de alto volume.
2. **Ciclo de Vida Corporativo (LTS):** Suporte estendido oficial com atualizações de segurança para ambientes financeiros de missão crítica.
3. **Compatibilidade com .NET 8:** Suporte pleno e testado com o driver de mercado `Npgsql` (v8.0.4) para tipos modernos do C# 12 (`DateOnly`, `Guid`).
4. **Segregação Granular de Privilégios (PoLP):** Suporte robusto a controle de acesso baseado em papéis (RBAC) para segregar o usuário de escrita (`cashflow_writer`) do usuário de leitura restrita (`cashflow_reader`).
5. **Superfície Mínima de Ataque em Containers:** Disponibilidade de imagens de container minimizadas e auditáveis (Alpine Linux).

---

## Decisão
Adotamos o **PostgreSQL 16** (especificamente a distribuição minimizada em container `postgres:16-alpine`) como o sistema gerenciador de banco de dados relacional (SGBD) da plataforma CashFlow.

### Justificativas Técnicas para a Escolha da Versão 16:

1. **Estabilidade, Maturidade e Ciclo de Vida LTS:**
   - O PostgreSQL 16 foi lançado em setembro de 2023 e possui suporte comunitário oficial garantido até **novembro de 2028** (5 anos de correções de segurança e patches de confiabilidade).
   - Em arquiteturas bancárias e financeiras, adota-se a política corporativa de versões amplamente provadas e maduras, evitando versões preliminares ou com ecossistema de drivers em maturação.

2. **Otimizações do Planejador de Consultas (Query Planner) e I/O:**
   - O PostgreSQL 16 introduziu aceleração de CPU via **instruções SIMD** para conversões de texto, parsing e agregações matemáticas (`SUM`, `COUNT`), acelerando consultas de agregação de consolidado.
   - Otimizações no gerenciamento de concorrência e buffer pool, reduzindo a contenção de I/O em cenários de gravação intensiva (`transactions`).
   - Melhorias substanciais em `VACUUM` concorrente e manutenção de índices btree.

3. **Governança de Segurança e Menor Privilégio:**
   - Refinamento do controle de permissões de catálogo e esquemas (`GRANT`/`REVOKE`), permitindo que a aplicação execute com usuários sem permissão de superusuário (`SUPERUSER`) e com privilégios limitados estritamente ao necessário (Write-Side vs Read-Side).
   - Suporte nativo aprimorado a autenticação criptográfica moderna (SCRAM-SHA-256).

4. **Sinergia com .NET 8 e Npgsql 8.x:**
   - O ecossistema `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.4` possui testes de regressão e otimizações de protocolo desenhados para o PostgreSQL 16.
   - Mapeamento nativo e performático do tipo C# `DateOnly` para a coluna PostgreSQL `date` sem conversões intermediárias de timestamp, evitando ambiguidades de fuso horário.

5. **Adoção da Imagem Docker `16-alpine`:**
   - Redução do footprint da imagem para ~100 MB (contra ~450 MB da imagem baseada em Debian), reduzindo expressivamente bibliotecas de sistema desnecessárias e potenciais vulnerabilidades conhecidas (CVEs).

---

## Consequências

### Positivas
- **Integridade Transacional Absoluta:** Garantia de persistência ACID em cada lançamento financeiro gravado pela API de Lançamentos.
- **Isolamento de Segurança:** Viabilização prática do Princípio do Menor Privilégio entre o Write-Side e o Read-Side em nível de catálogo do banco.
- **Longevidade e Manutenibilidade:** Ciclo de suporte garantido até o final de 2028 sem necessidade de upgrades disruptivos frequentes.
- **Desempenho Estável:** Otimizações de compilação JIT e vetorização SIMD para consultas sob demanda.

### Negativas / Mitigações
- **Consumo de I/O em Picos Severos de Consulta:** Consultas analíticas diretas poderiam degradar o banco se expostas diretamente a 50 RPS.
  - *Mitigação Adotada:* Conforme estabelecido na [ADR 002](ADR-002-caching-strategy.md), as consultas da API de Consolidado são interceptadas prioritariamente pelo cache distribuído Redis via estratégia Write-Through e Cache-Aside com proteção anti-stampede, relegando o PostgreSQL 16 a fallback seguro e armazenamento durável.
