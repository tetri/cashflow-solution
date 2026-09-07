import http from 'k6/http';
import { check, sleep } from 'k6';

// Teste de Ponto de Ruptura (Breakpoint / Capacity Limit Testing)
// Objetivo: Descobrir o limite maximo de vazao de 1 unica replica da API de Consolidado
// antes que ocorra degradacao acentuada de latencia (>50ms) ou perda de requisicoes (>5%),
// justificando matematicamente a necessidade de escalonamento horizontal.

export const options = {
  scenarios: {
    rampa_ponto_ruptura: {
      executor: 'ramping-arrival-rate',
      startRate: 50, // Inicia na meta nominal (50 RPS)
      timeUnit: '1s',
      preAllocatedVUs: 100,
      maxVUs: 500,
      stages: [
        { duration: '20s', target: 100 },  // Estagio 1: 50 -> 100 RPS (2x nominal)
        { duration: '20s', target: 250 },  // Estagio 2: 100 -> 250 RPS (5x nominal)
        { duration: '20s', target: 500 },  // Estagio 3: 250 -> 500 RPS (10x nominal)
        { duration: '20s', target: 800 },  // Estagio 4: 500 -> 800 RPS (16x nominal)
        { duration: '20s', target: 1200 }, // Estagio 5: 800 -> 1.200 RPS (24x nominal - Zona de Ruptura)
        { duration: '15s', target: 1200 }, // Sustentacao no topo extremo
        { duration: '15s', target: 0 },    // Desaceleracao suave
      ],
    },
  },
  thresholds: {
    // Afericao do criterio de SLA do desafio (< 5% de perda)
    http_req_failed: ['rate<0.05'],
    // Afericao da latencia alvo
    http_req_duration: ['p(95)<50', 'p(99)<100'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5002';
const MERCHANT_ID = 'LOJA_WSL_01';
const DATA_CONSULTA = '2026-09-05';

export default function () {
  const url = `${BASE_URL}/api/v1/consolidated/${MERCHANT_ID}/${DATA_CONSULTA}`;
  const params = {
    headers: {
      'Accept': 'application/json',
      'X-Api-Key': __ENV.API_KEY || 'cashflow-secret-api-key-2026',
    },
    tags: { name: 'ConsultaPontoRuptura' },
  };

  const resposta = http.get(url, params);

  check(resposta, {
    'status e 200': (r) => r.status === 200,
    'latencia aceitavel (<50ms)': (r) => r.timings.duration < 50,
  });

  sleep(0.01);
}
