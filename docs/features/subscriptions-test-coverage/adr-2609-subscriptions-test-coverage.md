# ADR-2609: Subscriptions test coverage scope

**Status**: Accepted
**Date**: 2026-07-05
**Feature**: subscriptions-test-coverage

## Context

A survey of e2e/conformance test coverage in other open-source FHIR servers —
fhir-candle (a Subscriptions reference implementation with a dedicated test
class, `R4TestSubscriptions`) and LinuxForHealth FHIR (a separate
`e2e-notifications` CI job) — found that ignixa-lab's bundled TestScript
suites have zero Subscriptions coverage. The 71-suite Microsoft FHIR Server
port (PR #5) doesn't cover it either, since Microsoft's own e2e suite doesn't
exercise Subscriptions in a form that ports cleanly to TestScript.

`ignixa-lab`'s TestScript engine executes suites as a sequence of outbound
HTTP requests against a target server (`RunRequest`); it has no ability to
stand up an HTTP endpoint of its own to *receive* an inbound callback. This is
the identical limitation already used to justify excluding
`Microsoft.Health.Fhir.Shared.Tests.Smart` (OAuth redirect/consent flows) and
`...Tests.Crucible` (external tool integration) from the Microsoft-suite port
— see the "Deliberately not covered" list in `backend/README.md`'s Suites
section. Subscriptions notification delivery is the same shape of problem:
the thing worth testing is asynchronous and inbound, and the engine is
synchronous and outbound-only.

Separately, FHIR's Subscription resource itself changed shape between
versions: R4/R4B use a simple `criteria` search-string plus a `channel` with
a `type` (rest-hook/websocket/email/message); R5 replaced `criteria` with a
mandatory `topic` canonical reference to a `SubscriptionTopic` resource, a
materially different authoring/setup burden (a topic resource must exist and
be resolvable before a subscription against it means anything).

## Options Considered

1. **No Subscriptions coverage at all** — leave the gap as identified by the
   research, add nothing. *(rejected: throws away the parts of Subscriptions
   that genuinely are testable — resource persistence, field round-tripping,
   capability advertisement — for no reason other than the harder parts being
   out of reach)*
2. **Full coverage including async delivery** — stand up a callback receiver
   inside the test run so the engine can assert a notification actually
   arrived. *(rejected for now: requires the runner to accept inbound HTTP
   traffic — a different architecture than today's outbound-only
   `RunRequest`, plus handling the handshake/heartbeat/error-retry semantics
   FHIR subscriptions define. Real scope of work, not a suite-authoring task.)*
3. **Basic round-trip coverage only, R4/R4B classic Subscription, gated on
   capability** — cover create/read/search/update/delete of a criteria-based
   Subscription; explicitly defer everything requiring inbound callbacks and
   the R5 topic-based model. *(chosen)*

## Decision

Adopt option 3. `Subscriptions/basic.json` covers the classic (R4/R4B)
criteria-based `Subscription` resource: create (PUT with a fixed id), read,
search by `_id`, update (criteria/channel/status), and delete — the same
CRUD-lifecycle shape as `CRUD/basic.json`. The whole suite is gated with
`http://ignixa.io/testscript/requiresCapability` on
`rest.resource.where(type='Subscription').exists()`, since Subscription
support (like `$reindex` and the other Microsoft-proprietary operations) is
not universal. Field-content assertions that depend on server-side status
management (e.g. whether a client-supplied `status: off` is honored verbatim)
are `warningOnly`.

**Explicitly deferred, not covered by this suite:**

- Actual notification delivery (rest-hook POST to the subscriber's endpoint,
  its payload shape, retry/backoff on delivery failure).
- The subscription handshake/heartbeat protocol.
- Subscription status transitions driven by the server (`requested` →
  `active`/`error`) in response to a real or attempted handshake.
- The R5 topic-based `Subscription` + `SubscriptionTopic` model, and R5's
  `$status`/`$events` operations.
- WebSocket and email channel types (only `rest-hook` is covered, since it's
  the only channel type expressible as a plain HTTP fixture).

## Consequences

- Closes the coverage gap identified against fhir-candle/LinuxForHealth for
  the part that's actually reachable from a black-box HTTP TestScript run,
  without pretending to validate delivery semantics the engine can't observe.
- The deferred items remain a real, documented gap. Covering them requires
  the runner to accept inbound HTTP callbacks during a run — a capability
  `Ignixa.TestScript`/the Functions host does not have today. If that
  capability is ever added (e.g. a short-lived callback listener spun up for
  the duration of a run, with its public URL substituted into the
  Subscription's `channel.endpoint`), this ADR's deferred list is the starting
  scope for the follow-up suite.
- R5 topic-based Subscription coverage is deferred as a separate concern from
  the async-delivery gap — it could in principle be added as a request/response
  suite (create a `SubscriptionTopic`, create a topic-based `Subscription`,
  read it back) without solving inbound callbacks at all, but is left out of
  this change to keep scope to the single gap the research identified.
