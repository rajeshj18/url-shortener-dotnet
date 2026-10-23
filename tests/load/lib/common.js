// Shared helpers for the k6 load tests (TSK-12, ADR-0006). k6 runs in a container on the compose network, so the
// services are reached by their compose names. Override with API_URL / REDIRECT_URL / API_KEY.
import http from 'k6/http';
import { check } from 'k6';

export const API = __ENV.API_URL || 'http://api:8080';
export const REDIRECT = __ENV.REDIRECT_URL || 'http://redirect:8080';
// The throwaway local dev key documented in the README (only its hash is configured).
const KEY = __ENV.API_KEY || 'local-dev-api-key-change-me';

const BASE62 = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';

// A browser-like User-Agent, so redirects count as clicks (bots are filtered out, ADR-0007).
export const BROWSER = { 'User-Agent': 'Mozilla/5.0 (X11; Linux x86_64) k6-load-test' };

/** Creates `count` links through the Api (in batches) and returns their codes. Runs in setup(), not measured. */
export function seed(count) {
  const codes = [];
  const batchSize = 50;
  for (let start = 0; start < count; start += batchSize) {
    const requests = [];
    for (let i = start; i < Math.min(count, start + batchSize); i++) {
      requests.push(createRequest(`https://example.com/load/${i}?seed=${Date.now()}`, 'seed'));
    }
    for (const response of http.batch(requests)) {
      if (response.status !== 201) {
        throw new Error(`seeding failed: ${response.status} ${response.body}`);
      }
      codes.push(response.json('code'));
    }
  }
  return codes;
}

/** A create request for http.request / http.batch. */
export function createRequest(longUrl, name) {
  return {
    method: 'POST',
    url: `${API}/api/v1/urls`,
    body: JSON.stringify({ longUrl }),
    params: { headers: { 'Content-Type': 'application/json', 'X-Api-Key': KEY }, tags: { name } },
  };
}

/** One redirect, not followed. */
export function redirect(code, name) {
  return http.get(`${REDIRECT}/${code}`, { redirects: 0, headers: BROWSER, tags: { name } });
}

/** A random 7-character generated-shape code (for the scan test: almost surely not a real link). */
export function randomCode() {
  let code = '';
  for (let i = 0; i < 7; i++) {
    code += BASE62[Math.floor(Math.random() * 62)];
  }
  return code;
}

/** Asserts one status; the share of passing checks is a threshold too. */
export function expectStatus(response, status) {
  return check(response, { [`status ${status}`]: (r) => r.status === status });
}

// A fresh service pays for JIT compilation and new database connections on its first requests. Each test
// therefore starts with a warm-up at the same rate; thresholds apply only to the measured scenario (filtered by
// k6's built-in `scenario` tag). The thresholds themselves are the SLOs and never change (TSK-12).
export const WARMUP = __ENV.WARMUP || '15s';

/** A constant-arrival-rate scenario: the rate is held regardless of latency (open model, like real traffic). */
export function constantRate(rate, duration, startTime, exec) {
  return {
    executor: 'constant-arrival-rate',
    rate,
    timeUnit: '1s',
    duration,
    startTime,
    exec: exec || 'default',
    preAllocatedVUs: Math.max(20, Math.ceil(rate / 10)),
    maxVUs: Math.max(100, rate),
  };
}

/** Warm-up, then the measured scenario named `name`, both at RATE (or the default). */
export function steadyRate(name, defaultRate, defaultDuration) {
  const rate = Number(__ENV.RATE || defaultRate);
  return {
    warmup: constantRate(rate, WARMUP, '0s'),
    [name]: constantRate(rate, __ENV.DURATION || defaultDuration, WARMUP),
  };
}
