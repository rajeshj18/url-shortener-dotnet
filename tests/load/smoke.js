// CI performance smoke (ADR-0012 §5, TSK-12): 30 s at a low rate, with the SLO thresholds. It catches big
// regressions on every PR; it is not a capacity test. P99_REDIRECT_MS / P99_CREATE_MS exist only for the
// one-off proof that the gate fails (an impossible threshold), never to loosen it.
import http from 'k6/http';
import { seed, redirect, createRequest, expectStatus, constantRate, WARMUP } from './lib/common.js';

http.setResponseCallback(http.expectedStatuses(201, 302));

const redirectP99 = Number(__ENV.P99_REDIRECT_MS || 50);
const createP99 = Number(__ENV.P99_CREATE_MS || 200);

export const options = {
  // Warm-up first (a CI stack always starts cold), then 30 s measured against the SLOs.
  scenarios: {
    warmup_redirects: constantRate(40, WARMUP, '0s', 'redirects'),
    warmup_creates: constantRate(10, WARMUP, '0s', 'creates'),
    redirects: constantRate(40, '30s', WARMUP, 'redirects'),
    creates: constantRate(10, '30s', WARMUP, 'creates'),
  },
  thresholds: {
    'http_req_duration{scenario:redirects}': [`p(99)<${redirectP99}`],
    'http_req_duration{scenario:creates}': [`p(99)<${createP99}`],
    'http_req_failed{scenario:redirects}': ['rate<0.01'],
    'http_req_failed{scenario:creates}': ['rate<0.01'],
  },
};

export function setup() {
  return { codes: seed(200) };
}

export function redirects(data) {
  expectStatus(redirect(data.codes[Math.floor(Math.random() * data.codes.length)], 'redirect'), 302);
}

export function creates() {
  const request = createRequest(`https://example.com/smoke/${__VU}/${__ITER}`, 'create');
  expectStatus(http.request(request.method, request.url, request.body, request.params), 201);
}
