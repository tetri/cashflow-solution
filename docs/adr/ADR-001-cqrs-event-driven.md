# ADR 001: Adoção do Padrão CQRS com Arquitetura Orientada a Eventos (EDA)

## Status
**Aprovado**

## Contexto
O sistema de controle de fluxo de caixa para comerciantes possui duas responsabilidades com características operacionais e requisitos de escalabilidade distintos:
1. **Serviço de Lançamentos (Write-Heavy / Alta Criticidade de Disponibilidade):** Registra débitos e créditos. Não pode sofrer degradação ou indisponibilidade decorrente de falhas ou lentidão na geração de relatórios consolidados.
2. **Serviço de Consolidado Diário (Read-Heavy / Alto Throughput):** Exige consultas em alta velocidade (picos de 50 req/s) do saldo diário acumulado.

Se ambos os módulos compartilhassem a mesma transação síncrona ou banco monolítico com lock em tabelas agregadas, uma sobrecarga nas leituras ou indisponibilidade no consolidado bloquearia a inserção de novos lançamentos de vendas do comerciante.

## Decisão
Adotamos o padrão **CQRS (Command Query Responsibility Segregation)** desacoplado via **Arquitetura Orientada a Eventos (EDA)** utilizando mensageria assíncrona (RabbitMQ/Kafka):
- **Command Side (Lançamentos):** Persiste a transação no PostgreSQL e publica o evento de domínio `TransactionCreatedEvent` no message broker. A operação de escrita retorna imediatamente sucesso (`201 Created`) com baixa latência e total isolamento.
- **Query Side (Consolidado Diário):** Um worker assíncrono consome o evento da fila, atualiza o modelo de leitura no PostgreSQL de forma idempotente e invalida/atualiza o cache no Redis. A API de Leitura consulta prioritariamente o cache distribuído.

```
[Comerciante] -> [Transactions API] -> (PostgreSQL DB)
                         |
                         v (Publica TransactionCreatedEvent)
                   [Message Broker]
                         |
                         v (Consome Evento)
                [Consolidated Worker] -> (PostgreSQL & Redis)
                         ^
                         | (Consulta Leitura Rápida)
[Comerciante] -> [Consolidated API]
```

## Consequências
### Positivas
- **Alta Disponibilidade e Desacoplamento:** O serviço de lançamentos continua operando 100% mesmo se o worker ou a API de consolidado estiverem fora do ar. As mensagens acumulam na fila e são processadas quando o serviço recuperar.
- **Escalabilidade Independente:** Lançamentos e leituras de consolidado podem escalar horizontalmente com réplicas e recursos distintos.
- **Performance Otimizada:** O modelo de escrita é enxuto (append-only), sem locks pesados de agregação.

### Negativas / Mitigações
- **Consistência Eventual:** Há um pequeno atraso (geralmente < 100ms) entre o lançamento e o reflexo no consolidado.
  - *Mitigação:* O atraso é imperceptível para relatórios diários de caixa e perfeitamente aceitável pelo domínio contábil/financeiro.
- **Complexidade de Mensageria:** Necessidade de gerenciar idempotência e dead-letter queues (DLQ).
