// Hot key: 30% of redirects hit one viral link (ADR-0006 criterion 3: p99 still <= 50 ms).
import http from 'k6/http';
import { seed, redirect, expectStatus, steadyRate } from './lib/common.js';

http.setResponseCallback(http.expectedStatuses(201, 302));

export const options = {
  scenarios: steadyRate('hotkey', 500, '2m'),
  thresholds: {
    'http_req_duration{scenario:hotkey}': ['p(99)<50'],
    'http_req_failed{scenario:hotkey}': ['rate<0.001'],
    checks: ['rate>0.999'],
  },
};

export function setup() {
  return { codes: seed(Number(__ENV.SEED || 2000)) };
}

export default function (data) {
  const code = Math.random() < 0.3 ? data.codes[0] : data.codes[Math.floor(Math.random() * data.codes.length)];
  expectStatus(redirect(code, 'redirect'), 302);
}
