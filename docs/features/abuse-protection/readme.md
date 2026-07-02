# Feature: Abuse protection (rate limiting)

**Status**: Proposed

## Problem

Ignixa Lab's backend exposes four anonymous HTTP endpoints. Two of them make
outbound HTTP calls to a **user-supplied** target FHIR server: `POST /api/run`
executes whole TestScript suites (dozens to hundreds of outbound requests per
run, held open for up to `HttpTimeoutSeconds` = 100s), and `GET /api/capability`
fetches `{target}/metadata`. The existing SSRF guard (`TargetUrlValidator`)
blocks private/loopback targets, but nothing stops a caller from:

- using the Function as a **DoS amplifier** against arbitrary *public* FHIR
  servers (one cheap POST fans out into many outbound requests), risking the
  Function's egress IP getting blocklisted;
- hammering the API itself, driving up compute cost and starving legitimate
  users.

There is currently **no rate limiting at any layer** — no per-client limit and
no aggregate cap. Endpoints differ wildly in cost: `/api/health` and
`/api/suites` are trivial/local; `/api/capability` is one outbound call;
`/api/run` is the primary abuse vector.

## Decision

Do **both** per-IP limiting and a global hourly cap, enforced as an in-process
`IFunctionsWorkerMiddleware` (sibling of `CorsMiddleware`) built on
`System.Threading.RateLimiting`: per-IP sliding windows tiered by endpoint
class (health exempt; suites/capability moderate; run strict), plus a
per-instance global hourly cap and a concurrency cap on `/api/run`. Limits are
configurable via `IgnixaLabOptions`. The in-memory counters are per-instance;
that gap is accepted for Phase 1 (the app runs at effectively one instance) and
a Table-Storage-backed distributed cap is defined as Phase 2 if scale-out or
observed abuse warrants it.

See [ADR-2608: Abuse protection and rate limiting](./adr-2608-abuse-protection.md).
