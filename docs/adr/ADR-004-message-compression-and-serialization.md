# ADR 004 - Estrategia de Serializacao e Compactacao na Troca de Mensagens

- **Status:** Aprovado / Documentado para Evolucao de Escala
- **Data:** 2026-09-05
- **Decisores:** Arquiteto de Software
- **Contexto:** Comunicacao assincrona interservicos via RabbitMQ (Write-Side -> Worker)

---

## 1. Contexto e Problematica

Na arquitetura orientada a eventos da solucao CashFlow, o servico de lancamentos (`Transactions.Api`) publica eventos de dominio (`TransactionCreatedEvent`) consumidos de forma assincrona pelo `Consolidated.Worker`.

Surgiu a questao tecnica sobre a adocao de algoritmos de compactacao de dados (como Brotli ou Gzip) e formatos de serializacao binarios (como Protocol Buffers ou Apache Avro) no transporte de mensagens pelo broker.

O desafio estabelece uma carga nominal de **50 requisicoes por segundo**. Cada evento unitario de transacao financeira contem:
- `eventId` (UUID): 36 caracteres
- `transactionId` (UUID): 36 caracteres
- `merchantId`: ~15 caracteres
- `amount`: valor decimal
- `type`: ~6 caracteres ("Credit" / "Debit")
- `description`: ~30 caracteres
- `createdAt` / `occurredOn`: timestamps ISO 8601

O tamanho total do payload JSON serializado em UTF-8 varia entre **180 e 240 bytes**.

---

## 2. Analise de Trade-offs: Compactacao em Mensagens Curtas

### 2.1 O Problema da Taxa de Compressao Negativa (Negative Compression Ratio)
Algoritmos de compressao modernos como Brotli, Snappy e Gstandard baseiam-se em tabelas de substituicao de strings repetidas (LZ77 / Huffman). 

Quando aplicados a payloads muito curtos (< 500 bytes):
1. Nao ha repeticao estatistica suficiente no payload para gerar ganhos de compressao.
2. Os metadados do algoritmo, dicionario e cabecalhos de frame adicionam de 20 a 50 bytes extras.
3. **Resultado:** Um JSON de 200 bytes compactado com Brotli frequentemente atinge **220 a 250 bytes** (taxa de compressao negativa), consumindo ciclos de CPU no Publisher e no Consumer sem nenhuma economia de rede.

### 2.2 Consumo de Recursos na Carga Nominal (50 RPS)
- Volume de dados transferido a 50 RPS em JSON: `50 req/s * 220 bytes = 11 KB/s`.
- O trafego de rede gerado e irrisorio para a infraestrutura de rede local ou VPC de nuvem (interfaces modernas operam a 1 Gbps / 10 Gbps).
- Adicionar compressao e descompressao cega nesse cenario representaria **desperdicio de CPU (overengineering)** sem contrapartida operacional.

---

## 3. Decisao Arquitetural Adotada

### 3.1 Fase Atual (Baseline Homologado)
- **Formato:** JSON minificado UTF-8 direto via `System.Text.Json` (zero indentacao e zero alocacoes desnecessarias no Garbage Collector).
- **Sem compressao de payload** para eventos unitarios de transacao.
- **Cabecalho AMQP:** `content-type: application/json; charset=utf-8`.
- **Beneficios:** Simplicidade de depuracao via interface web do RabbitMQ, menor latencia de processamento (< 1ms por mensagem) e minimo consumo de CPU.

### 3.2 Estrategia de Evolucao Futura: Compactacao por Limiar (Threshold Compression) e Protobuf

Quando o sistema evoluir para novos cenarios de escala e processamento em lote, serao ativados os seguintes mecanismos:

#### A. Protocol Buffers (Google Protobuf) para Mensagens Unitarias
- Substituicao do JSON por **Protobuf** para contratos interservicos de missao critica.
- O payload binario posicional de `TransactionCreatedEvent` cai de 220 bytes para **45 bytes** (reducao de quase 80%), com velocidade de parsing 5x a 8x superior sem gastar CPU com compactacao algoritmica.

#### B. Compactacao Brotli por Limiar (Threshold Compression)
- Para operacoes de lote contendo agrupamento de centenas de lancamentos (Batch Processing ou Replay de fila com payloads > 2 KB):
  - O publicador avalia se `payload.Length >= 2048 bytes`.
  - Se verdadeiro, aplica **BrotliStream** (.NET 8 nativo) e define o cabecalho AMQP `content-encoding: br`.
  - O consumidor inspeciona o header `content-encoding`: se for `"br"`, descompacta em stream antes da deserializacao; caso contrario, le diretamente.

---

## 4. Consequencias e Conformidade com o Desafio

- **Positivas:**
  - Evita gasto inutil de CPU e previne taxa de compressao negativa.
  - Mantem a arquitetura pragmatica, elegante e alinhada ao principio YAGNI (You Aren't Gonna Need It).
  - Demonstra capacidade analitica de tomada de decisao baseada em metricas de engenharia e custos operacionais de nuvem.
- **Negativas:**
  - Nenhuma no estagio atual, dado o volume nominal de trafego da aplicacao.
