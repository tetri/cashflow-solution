import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    rampa_progressiva: {
      executor: 'ramping-arrival-rate',
      startRate: 50,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 150,
      stages: [
        { duration: '15s', target: 100 },  // 100 RPS (2x meta)
        { duration: '15s', target: 200 },  // 200 RPS (4x meta)
        { duration: '15s', target: 350 },  // 350 RPS (7x meta)
        { duration: '15s', target: 500 },  // 500 RPS (10x meta)
        { duration: '15s', target: 650 },  // 650 RPS (13x meta - Limite de 1 node)
        { duration: '10s', target: 50 },   // Resfriamento
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    http_req_duration: ['p(95)<50'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5002';
const MERCHANT_ID = 'LOJA_WSL_01';
const DATA_CONSULTA = '2026-09-05';

export default function () {
  const url = `${BASE_URL}/api/v1/consolidated/${MERCHANT_ID}/${DATA_CONSULTA}`;
  const params = {
    headers: { 'Accept': 'application/json' },
    tags: { name: 'ConsultaPontoRuptura' },
  };

  const resposta = http.get(url, params);

  check(resposta, {
    'status e 200': (r) => r.status === 200,
    'tempo de resposta menor que 50ms': (r) => r.timings.duration < 50,
  });

  sleep(0.05);
}
