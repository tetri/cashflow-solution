# E2E Test Infra: CashFlow Solution

## Test Philosophy
- Opaque-box, requirement-driven. Sem dependência interna de implementação.
- Metodologia: Category-Partition + Boundary Value Analysis (BVA) + Pairwise Combinatorial Testing + Real-World Workload Testing.

## Feature Inventory
| # | Feature | Source (requirement) | Tier 1 | Tier 2 | Tier 3 |
|---|---------|---------------------|:------:|:------:|:------:|
| 1 | Registro de Crédito | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 2 | Registro de Débito | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 3 | Idempotência de Escrita | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 4 | Consolidado Diário (Cache Hit) | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 5 | Consolidado Diário (Fallback) | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 6 | Consumo Idempotente & DLQ | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 7 | Restrições pt-BR & Zero Emojis | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |

## Test Architecture
- Test runner: `dotnet test tests/CashFlow.E2ETests/CashFlow.E2ETests.csproj`
- Framework: xUnit + FluentAssertions + NSubstitute / HttpClient / Testcontainers
- Directory layout: `tests/CashFlow.E2ETests/`

## Real-World Application Scenarios (Tier 4)
| # | Scenario | Features Exercised | Complexity |
|---|----------|--------------------|------------|
| 1 | Dia comercial de varejo com múltiplas vendas e estornos concorrentes | F1, F2, F3, F4, F6 | Alta |
| 2 | Queda e recuperação do Redis com verificação de fallback transparente | F4, F5 | Alta |
| 3 | Ingestão de mensagens venenosas e direcionamento para DLQ | F6 | Média |
| 4 | Picos de consulta de consolidado (> 50 RPS) garantindo latência < 5ms | F4 | Alta |
| 5 | Repetição massiva de transações com a mesma IdempotencyKey | F1, F3 | Média |

## Coverage Thresholds
- Tier 1: >= 35 casos de teste (>= 5 por feature)
- Tier 2: >= 35 casos de teste (>= 5 por feature em condições de limite)
- Tier 3: >= 10 casos de teste (combinações par-a-par de funcionalidades)
- Tier 4: >= 5 cenários reais de negócio
- **Total mínimo esperado: >= 85 casos de teste E2E**
