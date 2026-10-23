// Steady creates (SLO: create p99 <= 200 ms). Each request makes a new link with a generated code.
import http from 'k6/http';
import { createRequest, expectStatus, steadyRate } from './lib/common.js';

http.setResponseCallback(http.expectedStatuses(201));

export const options = {
  scenarios: steadyRate('create', 100, '2m'),
  thresholds: {
    'http_req_duration{scenario:create}': ['p(99)<200'],
    'http_req_failed{scenario:create}': ['rate<0.001'],
    checks: ['rate>0.999'],
  },
};

export default function () {
  const request = createRequest(`https://example.com/created/${__VU}/${__ITER}`, 'create');
  expectStatus(http.request(request.method, request.url, request.body, request.params), 201);
}
