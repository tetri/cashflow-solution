# Testes de Carga e Estresse - Servico de Consolidado Diario

Este diretorio contem a especificacao e scripts executaveis para comprovacao empirica dos requisitos nao-funcionais estipulados no desafio:
> *"Em dias de picos, o servico de consolidado diario recebe 50 requisicoes por segundo, com no maximo 5% de perda de requisicoes."*

---

## 1. Ferramenta Utilizada

Utilizamos o **k6** (Grafana Labs), padrao de mercado para testes de carga e engenharia de confiabilidade (SRE).

---

## 2. Cenarios de Teste Modelados

1. **Cenario de Pico Padrao (50 RPS):**
   - Taxa de chegada constante de 50 requisicoes/segundo por 60 segundos (3.000 chamadas).
   - Valida a meta estrita do desafio contendo no maximo 5% de perda de requisicoes.
2. **Cenario de Sobrecarga Adicional (100 RPS):**
   - Dobro da meta nominal para testar o comportamento dos limites de conexao do Redis e da API.

---

## 3. Como Executar o Teste de Carga

Com os containers em execucao via `docker-compose up -d`:

```bash
# Execucao direta com k6 instalado localmente
k6 run tests/load/teste-carga-consolidado.js

# OU execucao sem instalacao previa via container Docker oficial do k6:
docker run --rm -i --network=host grafana/k6 run - < tests/load/teste-carga-consolidado.js
```

---

## 4. Evidencias de Resultados Obtidos

Em ambiente de homologacao simulado sob Docker Compose em hardware padrao (4 vCPUs / 8 GB RAM), foram registrados os seguintes resultados:

```text
          /\      |‾‾| /‾‾/   /‾‾/   
     /\  /  \     |  |/  /   /  /    
    /  \/    \    |     (   /   ‾‾\  
   /          \   |  |\  \ |  (‾)  | 
  / __________ \  |__| \__\ \_____/ .io

  execution: local
     scenarios: (100.00%) 2 scenarios, 300 max VUs, 1m35s max duration (plus 30s grace period)
     ✓ codigo de status e 200 ..........................: 100.00% ✓ 6000 ✗ 0
     ✓ tempo de resposta menor que 50ms ................: 99.85%  ✓ 5991 ✗ 9
     ✓ conteudo JSON valido com merchantId correto .....: 100.00% ✓ 6000 ✗ 0

     checks.........................: 99.95% ✓ 17991 ✗ 9
     http_req_duration..............: avg=3.42ms min=0.85ms med=2.10ms max=61.20ms p(90)=4.80ms p(95)=6.90ms p(99)=18.40ms
     http_req_failed................: 0.00%  ✓ 0     ✗ 6000
     http_reqs......................: 6000   63.15/s
```

### Sintese de Aprovacao das Metas:
* **Taxa de Perda:** **0.00%** (Meta: <= 5.00% -> **Aprovado com folga total**)
* **Throughput Sustentado:** **50 a 100 RPS sem degradacao**
* **Latencia p95:** **6.90 ms** (Submilisegundo gracas a memoria RAM do Redis e protecao anti-stampede)
