# Design: Dashboard visibility for grouped assertions and operation messages

**Date**: 2026-07-12
**Status**: Approved for implementation
**Related**: PR #32 (ignixa-fhir 0.6.19 migration), `frontend/src/components/TestRow.tsx`

## Problem

PR #32 surfaced two new TestScript extensions' data through the backend and TypeScript types, but the dashboard UI doesn't yet visualize either:

1. `assertionAnyOfGroup`/`assertionWhenResponseStatus` — `ConformanceStep.group_id`/`members` (populated by the mapper since PR #32's dashboard-plumbing task) are never read by `StepRow` in `TestRow.tsx`. A grouped assertion's aggregate message is visible (it's `step.message`), but the per-alternative detail — which member matched, why the others didn't — is invisible even though the data exists.
2. `waitFor` — investigation found `StepRow`'s render condition only shows `step.message` when there's **no** captured request/response: `{!step.request && !step.response && step.message ? <p>{step.message}</p> : null}`. Every operation step that actually executed (including every `waitFor` poll, timed out or not) has a captured exchange, so this condition never fires for operations — a `waitFor` timeout's explanatory message ("Timed out waiting for job completion after N attempts...") is currently hidden behind the request/response view with no visible explanation of why the step is flagged red. This isn't unique to `waitFor` (any operation failure with a captured request has the same problem), but `waitFor` timeouts are the concrete new case this migration introduces.

## Decision

Two changes, both scoped to `StepRow` in `frontend/src/components/TestRow.tsx`.

### 1. Group-member rendering

When `step.group_id` and `step.members` are present, the step's existing expand panel (`step__body`) gains a nested list below the aggregate message/exchange view — one row per member, reusing the Assertions tab's existing visual language (status chip + label + inline message) rather than inventing a new pattern. Still collapsed-by-default like every other step. New scoped CSS classes (`step__members`, `step__member`, etc.) rather than reusing the Assertions tab's classes directly, to avoid coupling two independently-evolving views to the same class names — but matching their visual weight (chip + label + message line).

Each member row shows:
- A pass/fail chip (green/red) — or a neutral "n/a" treatment when `applicable: false`, since that's neither a pass nor a fail, it's "this alternative's condition never matched."
- The member's `description`.
- The member's `message`, inline, when present (mirrors the Assertions tab's `assertion__meta` treatment).

### 2. Message visibility fix

`StepRow`'s message-rendering condition changes from:
```
{!step.request && !step.response && step.message ? <p className="step__message">{step.message}</p> : null}
```
to:
```
{step.message && (!hasExchange || step.status !== 'pass') ? <p className="step__message">{step.message}</p> : null}
```
where `hasExchange = step.request !== null || step.response !== null`, placed alongside (not instead of) the existing request/response rendering.

**Why not just `step.message && step.status !== 'pass'`:** assertion-kind steps never have a captured request/response, so the *old* condition was already unconditionally true for them regardless of pass/fail — a passing `assertionAnyOfGroup` aggregate's "matched alternative" message is exactly this case, and it already renders correctly today. Simplifying to a bare `status !== 'pass'` check would regress that (hiding the matched-alternative message on the common, happy-path case). The fix only needs to change behavior for steps that *do* have a captured exchange (operations) — assertion-only steps keep their existing always-show-when-present behavior. The actual `ConformanceStatus` union is `'pass' | 'fail' | 'error' | 'skipped'` — no separate `'warning'` value (warnings already roll up to `'pass'` server-side).

## Testing

No backend changes. `TestRow.tsx` has no existing component-test harness (checked — none exists in this repo for any component). This frontend has no dark/light theming system (checked `App.css` — no `prefers-color-scheme`/theme convention exists), so no theme-variant verification is needed. Verification is manual: run the frontend dev server, load a report containing a grouped assertion (e.g. `CRUD/delete.json`'s migrated test) and a non-pass operation with a message, confirm both render correctly.

## Out of Scope

- Any change to the backend, DTOs, or TypeScript types — those are already complete from PR #32.
- Rendering anything `waitFor`-specific beyond the general message-visibility fix (e.g. no new "N attempts" badge or polling-progress UI) — the existing message text already contains the attempt count, this fix just makes it visible.
