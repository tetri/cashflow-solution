#!/usr/bin/env bash
# ==============================================================================
# Bateria Automatizada de Testes de Carga e Estresse - CashFlow Solution
# Executa testes de leitura (Consolidado) e escrita (Transacoes) via k6 em container Docker
# ==============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

echo "======================================================================"
echo " Início da Bateria Completa de Testes de Carga (k6)"
echo " Data/Hora: $(date -u)"
echo "======================================================================"

echo ""
echo "[1/3] Informações do Ambiente Host e Virtualizado:"
echo "----------------------------------------------------------------------"
echo "Kernel: $(uname -r)"
echo "SO WSL: $(grep PRETTY_NAME /etc/os-release | cut -d= -f2 | tr -d '\"')"
echo "vCPUs Disponíveis: $(nproc)"
echo "Memória Total: $(free -h | awk '/^Mem:/ {print $2}')"
echo "Versão Docker: $(docker --version)"

echo ""
echo "[2/3] Executando Cenário de Leitura: Consolidado Diário (50 RPS e 100 RPS)..."
echo "----------------------------------------------------------------------"
cat "${SCRIPT_DIR}/teste-carga-consolidado.js" | docker run --rm -i \
  --network=host \
  -v "${SCRIPT_DIR}:/reports" \
  grafana/k6 run --summary-export=/reports/resultado-estresse.json -

echo ""
echo "[3/3] Executando Cenário de Escrita: Ingestão de Transações (30 RPS e 50 RPS)..."
echo "----------------------------------------------------------------------"
cat "${SCRIPT_DIR}/teste-carga-transacoes.js" | docker run --rm -i \
  --network=host \
  -v "${SCRIPT_DIR}:/reports" \
  grafana/k6 run --summary-export=/reports/resultado-estresse-transacoes.json -

echo ""
echo "======================================================================"
echo " Bateria de testes concluída com sucesso!"
echo " Relatórios estruturados salvos em tests/load/"
echo "======================================================================"
