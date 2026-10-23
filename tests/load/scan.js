// Scan: random codes that don't exist (ADR-0006 criterion 4). Every miss may try the primary only while the
// per-instance fallback budget (500/s) allows; the rest answer 404 at once. p99 must stay <= 50 ms; the
// granted/denied split is read from the redirect_primary_fallback metric after the run (see the results doc).
import http from 'k6/http';
import { redirect, randomCode, expectStatus, steadyRate } from './lib/common.js';

http.setResponseCallback(http.expectedStatuses(404));

export const options = {
  scenarios: steadyRate('scan', 1000, '2m'),
  thresholds: {
    'http_req_duration{scenario:scan}': ['p(99)<50'],
    'http_req_failed{scenario:scan}': ['rate<0.001'],
    checks: ['rate>0.999'],
  },
};

export default function () {
  expectStatus(redirect(randomCode(), 'scan'), 404);
}
