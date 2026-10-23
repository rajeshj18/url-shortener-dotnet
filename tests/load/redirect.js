// Steady redirect load on seeded links (ADR-0006 criterion 1: redirect p99 <= 50 ms at the tested rate).
import http from 'k6/http';
import { seed, redirect, expectStatus, steadyRate } from './lib/common.js';

http.setResponseCallback(http.expectedStatuses(201, 302));

export const options = {
  scenarios: steadyRate('redirect', 500, '2m'),
  thresholds: {
    'http_req_duration{scenario:redirect}': ['p(99)<50'],
    'http_req_failed{scenario:redirect}': ['rate<0.001'],
    checks: ['rate>0.999'],
  },
};

export function setup() {
  return { codes: seed(Number(__ENV.SEED || 2000)) };
}

export default function (data) {
  const code = data.codes[Math.floor(Math.random() * data.codes.length)];
  expectStatus(redirect(code, 'redirect'), 302);
}
