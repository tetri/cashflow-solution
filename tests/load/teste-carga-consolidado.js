import http from 'k6/http';
import { check, sleep } from 'k6';

// Configuracao do Teste de Carga para validacao dos requisitos nao-funcionais do desafio:
// - Picos de 50 requisicoes por segundo (RPS) sustentados com no maximo 5% de perda de requisicoes.
// - Validacao de latencia de p95 < 50ms gracas a camada de cache distribuido Redis (Cache-Aside).

export const options = {
  scenarios: {
    pico_cinquenta_rps: {
      executor: 'constant-arrival-rate',
      rate: 50, // 50 requisicoes por segundo (requisito exato do desafio)
      timeUnit: '1s',
      duration: '60s', // Teste continuo por 1 minuto (total de 3.000 requisicoes)
      preAllocatedVUs: 50,
      maxVUs: 100,
    },
    estresse_cem_rps: {
      executor: 'constant-arrival-rate',
      rate: 100, // Margem de seguranca de 100% (100 RPS)
      timeUnit: '1s',
      startTime: '65s',
      duration: '30s', // Teste de sobrecarga adicional
      preAllocatedVUs: 100,
      maxVUs: 200,
    },
  },
  thresholds: {
    // Requisito do desafio: no maximo 5% de perda de requisicoes (taxa de erro < 5%)
    http_req_failed: ['rate<0.05'],
    // Metrica de alta performance esperada para cache Redis: p95 inferior a 50ms
    http_req_duration: ['p(95)<50', 'p(99)<100'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5002';
const MERCHANT_ID = 'MERCHANT_LOADTEST_001';
const DATA_CONSULTA = '2026-09-05';

export default function () {
  const url = `${BASE_URL}/api/v1/consolidated/${MERCHANT_ID}/${DATA_CONSULTA}`;
  const params = {
    headers: {
      'Accept': 'application/json',
    },
    tags: { name: 'ObterConsolidadoDiario' },
  };

  const resposta = http.get(url, params);

  check(resposta, {
    'codigo de status e 200': (r) => r.status === 200,
    'tempo de resposta menor que 50ms': (r) => r.timings.duration < 50,
    'conteudo JSON valido com merchantId correto': (r) => {
      try {
        const corpo = JSON.parse(r.body);
        return corpo.merchantId === MERCHANT_ID;
      } catch (e) {
        return false;
      }
    },
  });

  sleep(0.1);
}
