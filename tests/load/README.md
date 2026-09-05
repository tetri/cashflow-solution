# Testes de Carga e Estresse - Serviço de Consolidado Diário e Transações

Este diretório contém a especificação, scripts executáveis e automação em lote para comprovação empírica dos requisitos não-funcionais estipulados no desafio:
> *"Em dias de picos, o serviço de consolidado diário recebe 50 requisições por segundo, com no máximo 5% de perda de requisições."*

---

## 1. Ferramenta Utilizada

Utilizamos o **k6** (Grafana Labs), padrão de mercado para testes de carga e engenharia de confiabilidade (SRE).

---

## 2. Cenários de Teste Disponíveis

1. **Cenário Read-Side (Consolidado Diário):** [`teste-carga-consolidado.js`](teste-carga-consolidado.js)
   - Pico nominal de 50 RPS durante 60 segundos (3.000 chamadas).
   - Estresse de sobrecarga a 100 RPS durante 30 segundos (3.000 chamadas adicionais).
2. **Cenário Write-Side (Ingestão de Transações):** [`teste-carga-transacoes.js`](teste-carga-transacoes.js)
   - 30 RPS durante 30 segundos (900 transações).
   - Pico de 50 RPS durante 25 segundos (1.250 transações adicionais).

---

## 3. Como Executar a Bateria Completa de Testes

Com os containers em execução via `docker-compose up -d`:

```bash
# Execucao automatizada de toda a bateria (Leitura + Escrita):
chmod +x tests/load/executar-bateria.sh
./tests/load/executar-bateria.sh
```

Ou executar cenários individuais via Docker k6:

```bash
# Apenas leitura de consolidado:
cat tests/load/teste-carga-consolidado.js | docker run --rm -i --network=host grafana/k6 run -

# Apenas escrita de transacoes:
cat tests/load/teste-carga-transacoes.js | docker run --rm -i --network=host grafana/k6 run -
```

---

## 4. Relatório Detalhado com Gráficos e Hardware Host

Para conferir o relatório completo com **especificação detalhada do hardware físico (CPU, RAM, Discos), métricas percentis (p50, p90, p95, p99) e gráficos de desempenho**, consulte o documento oficial:
* [Relatório Oficial de Teste de Estresse e Benchmark de Desempenho](../../docs/RELATORIO_ESTRESSE_BENCHMARK.md)
