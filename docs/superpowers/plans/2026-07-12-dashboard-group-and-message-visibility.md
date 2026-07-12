# Dashboard Group and Message Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the dashboard's `StepRow` component render `assertionAnyOfGroup` per-alternative diagnostics and surface operation failure messages (including `waitFor` timeouts) that are currently invisible.

**Architecture:** Two changes to one component (`frontend/src/components/TestRow.tsx`'s `StepRow`), plus matching CSS in `frontend/src/App.css`. No backend or type changes — both fields are already piped through from PR #32.

**Tech Stack:** React + TypeScript (frontend), no new dependencies.

**Design doc:** `docs/superpowers/specs/2026-07-12-dashboard-group-and-message-visibility-design.md` — read it first for full rationale, especially the corrected message-visibility condition (a naive `status !== 'pass'` check would regress passing grouped-assertion messages).

## Global Constraints

- No backend, DTO, or TypeScript-type changes — `ConformanceStep.group_id`/`members` and `ConformanceGroupMember` already exist in `frontend/src/types/conformance.ts`.
- New CSS classes are scoped separately (`step__members`, `step__member`, etc.) — do not reuse `.assertion__*` classes directly, to keep the two views independently stylable.
- No component-test harness exists in this repo for any frontend component (verified) — verification is manual via the dev server, not automated tests.
- This frontend has no dark/light theming system — no theme-variant verification needed.

---

### Task 1: Render group-member diagnostics and fix operation message visibility

**Files:**
- Modify: `frontend/src/components/TestRow.tsx` (the `StepRow` function, currently lines 126-178)
- Modify: `frontend/src/App.css` (add new rules after the existing `.step__message` rule, currently ending around line 1079)

**Interfaces:**
- Consumes: `ConformanceStep.group_id` (`string | null`), `ConformanceStep.members` (`ConformanceGroupMember[] | null`), `ConformanceGroupMember` (`{ description: string | null; applicable: boolean; passed: boolean; message: string | null }`) — all already defined in `frontend/src/types/conformance.ts`.
- Produces: nothing consumed elsewhere — this is the final piece of PR #32's dashboard-visibility follow-up.

- [ ] **Step 1: Replace `StepRow`'s body**

Read the current `StepRow` function in `frontend/src/components/TestRow.tsx` first to confirm it still matches what's below (it was last touched in PR #32's Task 3, no changes expected since) — the `hasDetail`/`title`/`headerContent` logic and the returned JSX's header portion (the `<div className={\`step step--${step.status}\`}>` wrapper through the `headerContent`) stay unchanged. Replace only the `hasDetail` declaration and the body-rendering block:

Current (to replace):
```tsx
  const hasDetail = step.request !== null || step.response !== null || Boolean(step.message);
```
becomes:
```tsx
  const hasExchange = step.request !== null || step.response !== null;
  const hasMembers = Boolean(step.members?.length);
  const hasDetail = hasExchange || Boolean(step.message) || hasMembers;
```

Current (to replace):
```tsx
      {hasDetail && open ? (
        <div className="step__body">
          {step.request ? <HttpRequestView request={step.request} /> : null}
          {step.response ? <HttpResponseView response={step.response} /> : null}
          {!step.request && !step.response && step.message ? (
            <p className="step__message">{step.message}</p>
          ) : null}
        </div>
      ) : null}
```
becomes:
```tsx
      {hasDetail && open ? (
        <div className="step__body">
          {step.request ? <HttpRequestView request={step.request} /> : null}
          {step.response ? <HttpResponseView response={step.response} /> : null}
          {step.message && (!hasExchange || step.status !== 'pass') ? (
            <p className="step__message">{step.message}</p>
          ) : null}
          {hasMembers ? (
            <div className="step__members">
              {step.members!.map((member, memberIndex) => (
                <div key={memberIndex} className="step__member">
                  <div className="step__member-header">
                    <span
                      className={`step__member-chip${
                        member.applicable ? (member.passed ? ' step__member-chip--pass' : ' step__member-chip--fail') : ''
                      }`}
                    >
                      {member.applicable ? (member.passed ? 'PASS' : 'FAIL') : 'N/A'}
                    </span>
                    <span className="step__member-label">{member.description ?? 'Alternative'}</span>
                  </div>
                  {member.message ? <span className="step__member-message">{member.message}</span> : null}
                </div>
              ))}
            </div>
          ) : null}
        </div>
      ) : null}
```

- [ ] **Step 2: Add the new CSS rules**

In `frontend/src/App.css`, immediately after the existing `.step__message` rule (currently lines 1075-1079):
```css
.step__message {
  margin: 10px 0 0;
  font-size: 12px;
  color: var(--text2);
}
```
add:
```css

.step__members {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 10px;
}

.step__member {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.step__member-header {
  display: flex;
  align-items: center;
  gap: 10px;
}

.step__member-chip {
  padding: 2px 8px;
  border-radius: 99px;
  font-size: 10px;
  font-weight: 700;
  flex: none;
  background: var(--inset);
  color: var(--text3);
}

.step__member-chip--pass {
  background: var(--pass-bg);
  color: var(--pass);
}

.step__member-chip--fail {
  background: var(--fail-bg);
  color: var(--fail);
}

.step__member-label {
  font-size: 12.5px;
  color: var(--text2);
  flex: 1;
}

.step__member-message {
  font-family: 'IBM Plex Mono', monospace;
  font-size: 10.5px;
  color: var(--text3);
}
```

These reuse the exact CSS custom properties (`--pass-bg`, `--pass`, `--fail-bg`, `--fail`, `--inset`, `--text2`, `--text3`) already used by the sibling `.step__chip`/`.assertion__chip` rules, so the new rows match the existing color system without introducing new tokens.

- [ ] **Step 3: Verify the build compiles**

Run: `cd frontend && npm run build`
Expected: SUCCESS — this catches any TypeScript error in the JSX (e.g. a typo in `member.applicable`/`member.passed`/`member.description`/`member.message`) before manual verification.

- [ ] **Step 4: Manual verification via the dev server**

Run: `cd frontend && npm run dev` (and separately, in another terminal, `cd backend/src/Ignixa.Lab.Functions && func start` if not already running, per `docs/development.md`).

In the dashboard UI:
1. Run the `CRUD/delete.json` suite (or any of the 5 suites migrated in PR #32) against a real target that reaches the delete test (e.g. `bkowitz-testdeploy.azurewebsites.net`) and expand its "delete removes the resource..." test's Steps tab. Confirm the grouped assertion step now shows a nested member list below its aggregate message, with correct PASS/FAIL/N/A chips per alternative.
2. Find or trigger an operation step with a non-pass status and a message (e.g. temporarily point at a target that will `waitFor`-timeout, or use any existing failed operation in a suite run) and confirm its message now renders in the expanded step body alongside the request/response view — it must NOT have rendered there before this change.
3. Confirm a *passing* operation step's message (if any exists — most won't have one) still does not clutter the view, and confirm a *passing* grouped-assertion step's aggregate message still renders (regression check for the corrected condition — this must not have broken).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/TestRow.tsx frontend/src/App.css
git commit -m "feat(dashboard): render assertionAnyOfGroup member diagnostics and fix operation message visibility"
```
