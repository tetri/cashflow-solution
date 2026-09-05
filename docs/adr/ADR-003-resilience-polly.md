# ADR 003: Uso de Polly para Tolerância a Falhas e Resiliência

## Status
**Aprovado**

## Contexto
Em uma arquitetura distribuída com microsserviços, falhas de rede, indisponibilidade transitória de bancos de dados, saturação do broker de mensageria ou lentidão no Redis são inevitáveis. Sem políticas de resiliência, falhas temporárias podem propagar em cascata, derrubando threads de execução das APIs e gerando perda de transações ou timeouts generalizados.

## Decisão
Adotamos a biblioteca **Polly (v8+)** no .NET 8, utilizando os seguintes padrões combinados em pipelines de resiliência (`ResiliencePipeline`):
1. **Exponential Backoff com Jitter (Retry Policy):**
   - Aplicado a conexões com o PostgreSQL, Redis e publicação no RabbitMQ.
   - 3 tentativas com atraso exponencial (ex: 200ms, 400ms, 800ms) adicionado de variação aleatória (*jitter*) para evitar efeito de manada (*thundering herd*).
2. **Circuit Breaker (Disjuntor):**
   - Configurado para monitorar a taxa de erros (ex: 50% de falhas em uma janela de 30s).
   - Quando ativado (aberto), rejeita chamadas imediatamente sem sobrecarregar o recurso instável durante 15s (*half-open* para validação).
3. **Fallback Policy & Timeout:**
   - Na leitura de consolidado, se o Redis estiver inacessível pelo Circuit Breaker, o fallback redireciona transparentemente a consulta para o PostgreSQL com degraded log.
   - Timeouts estritos de 3s por operação de I/O para evitar exaustão de threads.
4. **Dead Letter Queue (DLQ) & Poison Message Handling:**
   - No worker de mensagens, mensagens que falharem após todas as retentativas são encaminhadas para uma fila `cashflow.transactions.dlq` para análise e replay manual.

```
Request/Operação
       |
  [Rate Limiter] (Opcional)
       |
   [Timeout] (3 segundos)
       |
[Circuit Breaker] ---- Aberto ----> [Fallback Degradado]
       | (Fechado)
    [Retry] (3x com Exponential Backoff + Jitter)
       |
  Recurso Externo (DB / Redis / Broker)
```

## Consequências
### Positivas
- **Autocura (Self-Healing):** Falhas de rede de curtíssima duração são superadas sem intervenção humana ou erro para o usuário final.
- **Fail Fast:** Sob falhas graves e persistentes, o Circuit Breaker protege os recursos e responde rapidamente.
- **Zero Perda de Mensagens:** Mensagens de erro irrecuperável são preservadas na DLQ.

### Negativas / Mitigações
- **Complexidade de Configuração:** Necessidade de afinar métricas de sensibilidade para não disparar o disjuntor em falso.
