# ADR-2608: Abuse protection and rate limiting

**Status**: Proposed
**Date**: 2026-07-01
**Feature**: abuse-protection

## Context

The backend is a .NET 10 isolated-worker Functions app deployed to Azure App
Service (`backend-deploy.yml`, same hosting model as the existing
`ignixafhirpath` site). All four endpoints are `AuthorizationLevel.Anonymous`
and the frontend is a static GitHub Pages SPA, so there is no auth principal to
key limits on — the client IP is the only identity available.

Endpoint cost profile:

| Endpoint | Work | Abuse potential |
|---|---|---|
| `GET /api/health` | Reads an assembly attribute | None — also used as a liveness probe |
| `GET /api/suites` | Reads the bundled suite catalog (in-memory) | Low |
| `GET /api/capability` | One outbound `GET {target}/metadata` to a user-supplied server | Moderate |
| `POST /api/run` | Executes up to `MaxSuitesPerRun` (default **50**) TestScript suites, each issuing many outbound HTTP calls to a user-supplied server, with a 100s per-call timeout | **High — primary vector** |

`TargetUrlValidator` already blocks private/loopback/link-local targets (SSRF),
but a caller can still point `/api/run` at any *public* server. One cheap POST
fans out into potentially hundreds of outbound requests, so the Function is a
request amplifier: it can be used to degrade third-party FHIR servers and get
our egress IP blocklisted, and repeated runs burn our own compute.

Relevant platform facts:

- App Service can **scale out**; any in-process counter is per-instance, so an
  in-memory "global" cap is really `cap × instanceCount`. A true cross-instance
  cap needs a shared store. There is no HTTP session affinity for Functions, so
  even *per-IP* in-memory counters are split across instances (effective limit
  up to `N ×` the configured one).
- The socket peer (`HttpContext.Connection.RemoteIpAddress`) is the App Service
  front end, not the client. App Service **appends** the real client IP (as
  `ip:port`) to any incoming `X-Forwarded-For`; entries to the left of that are
  client-controlled and spoofable.
- `System.Threading.RateLimiting` ships in-box (fixed window, sliding window,
  token bucket, concurrency, `PartitionedRateLimiter`).
- `CorsMiddleware` already establishes the `IFunctionsWorkerMiddleware` seam in
  `Program.cs` (`builder.UseMiddleware<CorsMiddleware>()`); rate limiting fits
  as a sibling middleware ordered after it.

## Options Considered

### Where to enforce

1. **Azure API Management in front of the Function** — first-class
   `rate-limit-by-key` policies, per-subscription keys. *(rejected: a whole new
   billable resource plus policy/ops surface for a free lab tool; the deploy
   pipeline deliberately creates no Azure resources)*
2. **Azure Front Door / WAF rate-limit rules** — edge enforcement, trusted
   client IP, absorbs floods before they reach compute. *(deferred: right
   answer if this ever needs hardened edge protection, but it adds a standing
   monthly cost and infra the project doesn't manage today; kept as Phase 3)*
3. **App Service access restrictions / IP restrictions** — static allow/deny
   lists only; no rate dimension. *(rejected: wrong tool)*
4. **In-process `IFunctionsWorkerMiddleware` using
   `System.Threading.RateLimiting`** — zero new infrastructure, follows the
   existing `CorsMiddleware` pattern, tiered per endpoint, fully configurable
   via `IgnixaLabOptions`. Weakness: counters are per-instance. *(chosen for
   Phase 1)*

### Whether to limit per-IP, globally, or both

1. **Per-IP only** — fair to individual clients but a botnet (many IPs) can
   still exhaust aggregate outbound capacity and egress reputation.
2. **Global only** — caps aggregate harm but one abuser consumes the whole
   budget and starves everyone.
3. **Both** *(chosen)* — per-IP limits provide fairness and stop the common
   single-abuser case; the global cap on `/api/run` bounds worst-case
   aggregate outbound traffic (the amplifier concern) regardless of how many
   IPs participate. Each layer covers the other's blind spot.

### Algorithm

- **Fixed window** — trivial, but permits a 2× burst straddling the window
  boundary. Acceptable for a coarse hourly cap; too sloppy for per-minute
  limits on the expensive endpoint.
- **Sliding window** *(chosen for per-IP limits)* — smooths the boundary-burst
  problem at negligible cost; in-box via `SlidingWindowRateLimiter`.
- **Token bucket** — best for "steady rate + occasional burst" traffic shapes;
  more knobs (refill rate, burst size) than this API needs. Not chosen; the
  sliding window's implicit burst tolerance is enough.
- **Fixed window aligned to the clock hour** *(chosen for the global run cap)*
  — matches the "N runs per hour" mental model, is trivially explainable, and
  is the only shape that maps cleanly onto a distributed store row
  (`PartitionKey = "run-global"`, `RowKey = yyyyMMddHH`) in Phase 2.
- **Concurrency limiter** *(chosen, in addition)* — a run holds outbound HTTP
  for up to 100s per call; bounding *simultaneous* runs is the single most
  direct control on amplifier output and instance resource pressure. In-box
  via `ConcurrencyLimiter` (no queueing — reject immediately with 429).

## Decision

Adopt **both** per-IP and global limiting, enforced by a new
`RateLimitMiddleware : IFunctionsWorkerMiddleware` registered immediately
after `CorsMiddleware`, built entirely on `System.Threading.RateLimiting`.
No new Azure resources in Phase 1.

### 1. Endpoint classes and starting limits

Requests are classified by function name (`context.FunctionDefinition.Name`),
not by URL parsing. Proposed defaults (all configurable, see §4):

| Class | Endpoints | Per-IP limit | Global limit | Notes |
|---|---|---|---|---|
| Exempt | `Health`, all CORS preflight `OPTIONS` | — | — | Health is a liveness probe; preflights are terminated by `CorsMiddleware` before this middleware runs |
| Light | `Suites` | 30 / min (sliding) | — | Local, cheap; limit exists only to blunt dumb loops |
| Moderate | `Capability` | 12 / min (sliding) | — | One outbound call each |
| Strict | `Run` | 4 / min (sliding) **and** 20 / hour (sliding) | 100 / hour (fixed, clock-aligned) **and** max 4 concurrent | The per-hour per-IP tier stops slow-drip abuse that the per-minute tier alone would admit (240/hour) |

Sizing rationale: a legitimate user iterating on a server fix runs a suite,
reads the report, adjusts, and reruns — single-digit runs per minute and a few
dozen per hour is generous for that loop. The global 100/hour cap bounds
worst-case outbound amplification at `100 × MaxSuitesPerRun × per-suite
requests` per hour while requiring at least five distinct IPs
(`100 / 20`) to exhaust — so one abuser cannot starve the pool before their
per-IP limit trips.

### 2. Middleware mechanics

- One `PartitionedRateLimiter<(string ipKey, EndpointClass cls)>` (or
  equivalent chained limiters via `PartitionedRateLimiter.CreateChained`) held
  as a singleton; partitions are created lazily per key, and idle-partition
  eviction is handled by the built-in limiter.
- Acquire with `AttemptAcquire(permitCount: 1)` — never queue. Queuing would
  hold worker invocations hostage; rejecting fast is the point.
- Ordering: `CorsMiddleware` → `RateLimitMiddleware` → function. This keeps
  preflights exempt and — because CORS headers are written before `next()` —
  ensures the browser SPA can actually *read* 429 responses cross-origin.
- Non-HTTP invocations (`context.GetHttpContext() is null`) pass through,
  mirroring `CorsMiddleware`.
- A single kill switch (`Enabled = false`) bypasses everything, for local dev
  and incident response.

### 3. Client-IP extraction and spoofing

- Take the **right-most** entry of `X-Forwarded-For` and strip the `:port`
  suffix (App Service formats entries as `ip:port`; handle bracketed IPv6).
  App Service's front end *appends* the true client IP, so the right-most
  entry is the only one it vouches for; everything left of it is
  client-supplied and must be ignored.
- If `X-Forwarded-For` is absent (local dev, integration tests), fall back to
  `HttpContext.Connection.RemoteIpAddress`.
- If the IP cannot be parsed at all, bucket the request under a single
  `"unknown"` partition (strict-class limits) rather than exempting it —
  unparseable ≠ unlimited.
- Normalize IPv6 clients to their /64 prefix before keying; per-address IPv6
  limiting is trivially evaded by rotating within a delegated prefix.
- **Spoofing caveat (documented, accepted):** XFF is only trustworthy because
  App Service appends to it. If the app is ever fronted by Front Door, the
  right-most hop becomes Front Door's address and extraction must move one
  trusted hop left (or use Front Door's client-IP header) — this is called out
  as a Phase 3 change, not silently assumed.

### 4. Configuration (`IgnixaLabOptions` extension)

A nested options class bound from `IgnixaLab:RateLimiting`, following the
existing flat-defaults style:

```csharp
public sealed class RateLimitingOptions
{
    public bool Enabled { get; set; } = true;
    public int SuitesPerMinutePerIp { get; set; } = 30;
    public int CapabilityPerMinutePerIp { get; set; } = 12;
    public int RunPerMinutePerIp { get; set; } = 4;
    public int RunPerHourPerIp { get; set; } = 20;
    public int RunGlobalPerHour { get; set; } = 100;
    public int RunMaxConcurrent { get; set; } = 4;
}
```

exposed as `public RateLimitingOptions RateLimiting { get; set; } = new();` on
`IgnixaLabOptions`, so Azure app settings like
`IgnixaLab:RateLimiting:RunGlobalPerHour` override defaults per environment.

### 5. Response semantics

On rejection:

- **429 Too Many Requests**.
- **`Retry-After`** header in seconds, from the limiter's
  `MetadataName.RetryAfter` lease metadata when available; otherwise the
  remaining time in the current window (global cap) or the window length
  (per-IP), rounded up.
- JSON body matching the API's existing error shape:
  `{ "error": "Rate limit exceeded for this endpoint. Retry after 42 seconds." }`.
- One `LogWarning` per rejection with endpoint class and (hashed or truncated)
  IP key — enough for an App Insights alert on rejection rate, without turning
  the log into a PII ledger.

### 6. Multi-instance correctness — stated, not hand-waved

In-memory counters are **per-instance**. Consequences:

- Per-IP limits: requests from one IP spread across `N` instances, so the
  effective limit is up to `N ×` the configured value.
- "Global" hourly cap: actually `RunGlobalPerHour × N`.

**When that gap is acceptable:** this app is a low-traffic lab tool expected to
run at one (occasionally two) instances. At `N ≤ 2` every limit is within 2×
of intent — the limits are abuse *dampers*, not billing-grade quotas, and 2×
slack does not change the threat outcome. Phase 1 therefore accepts the gap
and pins it by capping scale-out (App Service plan max instance count / the
Flex `maximumInstanceCount` setting) at a small number.

**When it is not:** if the app is allowed to scale to many instances, or if
egress-reputation protection must be a hard guarantee, the global cap must
move to a shared store — Phase 2 below.

### 7. Failure modes and edge cases

- **Shared IPs (corporate NAT, university egress):** many legitimate users
  share one budget. Mitigations: light/moderate classes are generous;
  `Retry-After` makes back-off visible in the UI; per-IP hourly run budget
  (20) still supports several people iterating. If a real cohort hits this,
  the answer is per-user keys (Phase 3), not bigger IP buckets.
- **Global cap starving legitimate users:** bounded by design — per-IP hourly
  limits mean no single client can drain more than 20% of the global budget,
  and the clock-aligned window means starvation self-heals at the top of the
  hour with an accurate `Retry-After`.
- **Cold start / restart / instance recycle:** in-memory counters reset to
  zero — brief *under*-enforcement, never a lockout. Accepted (fail-open
  bias); an abuser cannot force recycles cheaply enough to matter at these
  budgets.
- **Distributed store down (Phase 2):** the Table Storage global counter
  **fails open** — log the failure, fall back to the in-memory per-instance
  cap (which stays in place as a floor), and alert. Rationale: availability
  of a lab tool outweighs strict cap enforcement, and the per-IP layer still
  holds. Revisit fail-closed only if egress blocklisting is ever actually
  observed.
- **IPv6 rotation:** addressed by /64 keying (§3).
- **Health probes:** exempt class, so platform liveness checks can never be
  rate-limited into a false "unhealthy" signal.

### 8. Phased rollout

- **Phase 1 — ship now (this ADR's scope):** `RateLimitMiddleware` +
  `RateLimitingOptions` as specified above; per-IP sliding windows, in-memory
  global hourly cap and concurrency cap on `Run`; 429/`Retry-After`/JSON error
  semantics; scale-out pinned small; unit tests around IP extraction,
  classification, and limiter behavior.
- **Phase 2 — distributed global cap (trigger: scale-out > 2 instances, or
  observed abuse):** back `RunGlobalPerHour` with an Azure Table Storage
  counter in the storage account the Functions host already requires
  (`AzureWebJobsStorage`) — one entity per clock hour
  (`PartitionKey = "run-global"`, `RowKey = yyyyMMddHH`), incremented with
  ETag optimistic-concurrency retry. No new infrastructure; fail-open per §7.
  (Redis would be lower-latency but is a new billable resource this app does
  not otherwise need.)
- **Phase 3 — edge hardening (trigger: sustained hostile traffic or need for
  trusted client identity):** Azure Front Door with WAF rate-limit rules in
  front of the app (moving IP extraction to Front Door's trusted hop, per §3),
  and/or lightweight API keys for heavy legitimate users so shared-NAT cohorts
  stop competing for one IP bucket.

## Consequences

- The worker gains one middleware, one options class extension, and a
  dependency on in-box `System.Threading.RateLimiting` — no new Azure
  resources, packages, or deployment changes in Phase 1.
- `/api/run` abuse is bounded on three axes (per-IP rate, aggregate hourly
  volume, concurrency), directly limiting the DoS-amplifier exposure and
  egress-reputation risk.
- Limits are per-instance until Phase 2; operators must keep the scale-out cap
  small for the numbers in §1 to mean what they say. This constraint must be
  documented in `backend/README.md` alongside the CORS notes.
- The frontend should learn to surface 429 + `Retry-After` as a friendly
  "slow down" message; until then users see the raw JSON error.
- Legitimate high-volume users (CI pipelines running conformance checks) will
  hit the strict `Run` limits by design; the escape hatch is a per-environment
  config override now, API keys in Phase 3.
