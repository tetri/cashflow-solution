# ADR 002: Estratégia de Cache Distribuído com Redis para o Consolidado Diário

## Status
**Aprovado**

## Contexto
O requisito não-funcional exige que o Serviço de Consolidado Diário suporte picos de **50 requisições por segundo (RPS)** com perda máxima admitida de 5% e latência sub-milisegundo. Consultar o banco relacional (PostgreSQL) com queries de agregação (`SUM()`, `COUNT()`) a cada requisição causaria contenção de I/O, alto uso de CPU e saturação do pool de conexões sob carga concorrente.

## Decisão
Adotamos uma estratégia híbrida de **Cache-Aside** combinado com **Event-Driven Cache Refresh/Write-Through** utilizando **Redis**:
1. **Chave de Cache Estruturada:** `consolidated:{merchantId}:{yyyy-MM-dd}`.
2. **Event-Driven Write Update:** Sempre que o `Consolidated Worker` processa um `TransactionCreatedEvent`, ele atualiza a tabela agregada no PostgreSQL e simultaneamente atualiza ou invalida a chave correspondente no Redis.
3. **Leitura com Cache-Aside & TTL Inteligente:**
   - A `Consolidated API` busca primeiro no Redis.
   - Caso seja um *cache miss* (ex: chave expirou ou primeiro acesso do dia), busca no PostgreSQL e armazena no Redis com TTL de **1 hora** (para o dia corrente) e **24 horas** (para dias passados, que são imutáveis).
4. **Serialização Otimizada:** Armazenamento em formato JSON binário / MessagePack leve.

```
Request GET /api/v1/consolidated/{merchantId}/{date}
        |
        v
 [Redis Cache] ---- Hit ----> Retorna < 5ms
        |
      Miss
        v
 [PostgreSQL] -------------> Salva no Redis e Retorna
```

## Consequências
### Positivas
- **Baixíssima Latência:** Respostas servidas da memória RAM do Redis em < 5ms.
- **Suporte a Alto Throughput:** Capacidade nativa de suportar milhares de RPS sem onerar o banco relacional.
- **Proteção do Banco:** Redução drástica da carga no PostgreSQL durante picos de consulta.

### Negativas / Mitigações
- **Possibilidade de Cache Stampede:** Múltiplas requisições simultâneas em caso de expiração simultânea.
  - *Mitigação:* Uso de locking distribuído ou pre-warming de cache na ingestão do evento.
- **Invalidação de Cache:** Risco de dados defasados caso a atualização falhe.
  - *Mitigação:* TTL explícito garante auto-recuperação e o worker atualiza a chave atômica via pipeline.
