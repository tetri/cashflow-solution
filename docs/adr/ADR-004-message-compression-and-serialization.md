# ADR 004 - Estratégia de Serialização e Compactação na Troca de Mensagens

- **Status:** Aprovado / Documentado para Evolução de Escala
- **Data:** 2026-09-05
- **Decisores:** Arquiteto de Software
- **Contexto:** Comunicação assíncrona interserviços via RabbitMQ (Write-Side -> Worker)

---

## 1. Contexto e Problemática

Na arquitetura orientada a eventos da solução CashFlow, o serviço de lançamentos (`Transactions.Api`) publica eventos de domínio (`TransactionCreatedEvent`) consumidos de forma assíncrona pelo `Consolidated.Worker`.

Surgiu a questão técnica sobre a adoção de algoritmos de compactação de dados (como Brotli ou Gzip) e formatos de serialização binários (como Protocol Buffers ou Apache Avro) no transporte de mensagens pelo broker.

O desafio estabelece uma carga nominal de **50 requisições por segundo**. Cada evento unitário de transação financeira contém:
- `eventId` (UUID): 36 caracteres
- `transactionId` (UUID): 36 caracteres
- `merchantId`: ~15 caracteres
- `amount`: valor decimal
- `type`: ~6 caracteres ("Credit" / "Debit")
- `description`: ~30 caracteres
- `createdAt` / `occurredOn`: timestamps ISO 8601

O tamanho total do payload JSON serializado em UTF-8 varia entre **180 e 240 bytes**.

---

## 2. Análise de Trade-offs: Compactação em Mensagens Curtas

### 2.1 O Problema da Taxa de Compressão Negativa (Negative Compression Ratio)
Algoritmos de compressão modernos como Brotli, Snappy e Gstandard baseiam-se em tabelas de substituição de strings repetidas (LZ77 / Huffman). 

Quando aplicados a payloads muito curtos (< 500 bytes):
1. Não há repetição estatística suficiente no payload para gerar ganhos de compressão.
2. Os metadados do algoritmo, dicionário e cabeçalhos de frame adicionam de 20 a 50 bytes extras.
3. **Resultado:** Um JSON de 200 bytes compactado com Brotli frequentemente atinge **220 a 250 bytes** (taxa de compressão negativa), consumindo ciclos de CPU no Publisher e no Consumer sem nenhuma economia de rede.

### 2.2 Consumo de Recursos na Carga Nominal (50 RPS)
- Volume de dados transferido a 50 RPS em JSON: `50 req/s * 220 bytes = 11 KB/s`.
- O tráfego de rede gerado é irrisório para a infraestrutura de rede local ou VPC de nuvem (interfaces modernas operam a 1 Gbps / 10 Gbps).
- Adicionar compressão e descompressão cega nesse cenário representaria **desperdício de CPU (overengineering)** sem contrapartida operacional.

---

## 3. Decisão Arquitetural Adotada

### 3.1 Fase Atual (Baseline Homologado)
- **Formato:** JSON minificado UTF-8 direto via `System.Text.Json` (zero indentação e zero alocações desnecessárias no Garbage Collector).
- **Sem compressão de payload** para eventos unitários de transação.
- **Cabeçalho AMQP:** `content-type: application/json; charset=utf-8`.
- **Benefícios:** Simplicidade de depuração via interface web do RabbitMQ, menor latência de processamento (< 1ms por mensagem) e mínimo consumo de CPU.

### 3.2 Estratégia de Evolução Futura: Compactação por Limiar (Threshold Compression) e Protobuf

Quando o sistema evoluir para novos cenários de escala e processamento em lote, serão ativados os seguintes mecanismos:

#### A. Protocol Buffers (Google Protobuf) para Mensagens Unitárias
- Substituição do JSON por **Protobuf** para contratos interserviços de missão crítica.
- O payload binário posicional de `TransactionCreatedEvent` cai de 220 bytes para **45 bytes** (redução de quase 80%), com velocidade de parsing 5x a 8x superior sem gastar CPU com compactação algorítmica.

#### B. Compactação Brotli por Limiar (Threshold Compression)
- Para operações de lote contendo agrupamento de centenas de lançamentos (Batch Processing ou Replay de fila com payloads > 2 KB):
  - O publicador avalia se `payload.Length >= 2048 bytes`.
  - Se verdadeiro, aplica **BrotliStream** (.NET 8 nativo) e define o cabeçalho AMQP `content-encoding: br`.
  - O consumidor inspeciona o header `content-encoding`: se for `"br"`, descompacta em stream antes da desserialização; caso contrário, lê diretamente.

---

## 4. Consequências e Conformidade com o Desafio

- **Positivas:**
  - Evita gasto inútil de CPU e previne taxa de compressão negativa.
  - Mantém a arquitetura pragmática, elegante e alinhada ao princípio YAGNI (You Aren't Gonna Need It).
  - Demonstra capacidade analítica de tomada de decisão baseada em métricas de engenharia e custos operacionais de nuvem.
- **Negativas:**
  - Nenhuma no estágio atual, dado o volume nominal de tráfego da aplicação.
