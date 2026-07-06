# Feature: Subscriptions test coverage

**Status**: Decided

## Problem

A survey of e2e/conformance test coverage in other open-source FHIR servers
(fhir-candle, LinuxForHealth FHIR) identified FHIR Subscriptions as a gap in
ignixa-lab's bundled TestScript suites — the 71-suite Microsoft FHIR Server
port (PR #5) has no Subscriptions coverage at all, and the two other servers
both ship real Subscriptions functionality with tests behind it. But
`ignixa-lab`'s TestScript runner is a black-box HTTP client (`RunRequest`) with
no inbound-callback support, so the interesting part of Subscriptions —
actual async notification delivery — can't be exercised the way CRUD/search
operations can.

## Decision

Add a minimal `Subscriptions` category (`Subscriptions/basic.json`) covering
only what a request/response TestScript run can actually assert: create,
read, search-by-`_id`, update, and delete round-tripping of a classic
(R4/R4B, criteria-based) `Subscription` resource, gated with
`requiresCapability` since the resource type isn't universally implemented.
Everything that requires receiving an inbound callback, or the R5 topic-based
Subscription model, is explicitly deferred — see
[ADR-2609](./adr-2609-subscriptions-test-coverage.md).

This mirrors the precedent already set for `Microsoft.Health.Fhir.Shared.Tests.Smart`
and `...Tests.Crucible` in PR #5 (excluded from the Microsoft-suite port for the
same "black-box HTTP TestScript engine can't drive this" reason, documented in
`backend/README.md`'s Suites section) rather than inventing a new exclusion
rationale.
