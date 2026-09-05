import http from 'k6/http';
import { check, sleep } from 'k6';

// Configuracao do teste de estresse para ingestao concorrente de transacoes (Write Side):
// - Testa o pipeline de validacao de payload, gravacao transacional no PostgreSQL
//   e disparo assincrono de mensagens no RabbitMQ.

export const options = {
  scenarios: {
    ingestao_trinta_rps: {
      executor: 'constant-arrival-rate',
      rate: 30, // 30 transacoes financeiras/segundo
      timeUnit: '1s',
      duration: '30s', // 900 transacoes totais
      preAllocatedVUs: 30,
      maxVUs: 60,
    },
    ingestao_cinquenta_rps: {
      executor: 'constant-arrival-rate',
      rate: 50, // 50 transacoes financeiras/segundo
      timeUnit: '1s',
      startTime: '35s',
      duration: '25s', // 1.250 transacoes totais
      preAllocatedVUs: 50,
      maxVUs: 100,
    },
  },
  thresholds: {
    // Tolerancia a falhas em gravacao: taxa de erro < 1%
    http_req_failed: ['rate<0.01'],
    // Escrita com transacao de banco e broker de mensageria: p95 < 200ms
    http_req_duration: ['p(95)<200', 'p(99)<500'],
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5001';

export default function () {
  const url = `${BASE_URL}/api/v1/transactions`;
  const isCredit = Math.random() > 0.4;
  const payload = JSON.stringify({
    merchantId: 'LOJA_BENCHMARK_STRESS',
    amount: parseFloat((Math.random() * 500 + 10).toFixed(2)),
    type: isCredit ? 'Credit' : 'Debit',
    description: `Lancamento automatico benchmark ${Date.now()}`,
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    tags: { name: 'CriarTransacao' },
  };

  const resposta = http.post(url, payload, params);

  check(resposta, {
    'status e 201 Created': (r) => r.status === 201,
    'retornou transactionId': (r) => {
      try {
        const body = JSON.parse(r.body);
        return body.transactionId !== undefined && body.transactionId !== '';
      } catch (e) {
        return false;
      }
    },
  });

  sleep(0.05);
}
