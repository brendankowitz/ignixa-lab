# Responsive UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair medium, small, and mobile rendering across the Conformance and Benches frontends, verified with Playwright viewport checks.

**Architecture:** Keep the existing two-app structure and fix responsive behavior at the current layout boundaries. Use CSS media queries for the Conformance app, existing inline-style helpers plus `useIsNarrowViewport` for the Benches app, and a committed Playwright smoke script to catch horizontal overflow at key breakpoints.

**Tech Stack:** React 19, TypeScript, Vite, CSS, Playwright, npm scripts.

---

## File structure

- Modify `frontend/package.json` and `frontend/package-lock.json` to add Playwright and an npm verification script.
- Create `frontend/scripts/responsive-check.mjs` as the Playwright smoke harness. It owns viewport navigation, horizontal-overflow assertions, and basic visibility checks for both app entry points.
- Modify `frontend/src/App.css` for Conformance responsive CSS only. Keep desktop rules intact and add medium/small/mobile overrides near the existing mobile block.
- Modify `frontend/src/benches/components/styles.ts` and `frontend/src/benches/components/primitives.tsx` for shared shrink-safe bench primitives.
- Modify `frontend/src/benches/BenchesApp.tsx` for top-bar wrapping and button shrink behavior.
- Modify `frontend/src/benches/validation/ValidationBench.tsx`, `frontend/src/benches/fhirpath/FhirPathBench.tsx`, and `frontend/src/benches/fakes/FakesBench.tsx` only where shared helpers cannot solve row wrapping.

## Task 1: Add Playwright responsive smoke harness

**Files:**
- Modify: `frontend/package.json`
- Modify: `frontend/package-lock.json`
- Create: `frontend/scripts/responsive-check.mjs`

- [ ] **Step 1: Install Playwright as a dev dependency**

Run:

```powershell
Set-Location frontend
npm install --save-dev playwright
```

Expected: `package.json` and `package-lock.json` include `playwright` under dev dependencies.

- [ ] **Step 2: Add the npm script**

In `frontend/package.json`, update the `scripts` block to include:

```json
"responsive:check": "playwright install chromium && node scripts/responsive-check.mjs"
```

Expected scripts:

```json
{
  "dev": "vite",
  "build": "tsc -b && vite build",
  "lint": "oxlint",
  "preview": "vite preview",
  "responsive:check": "playwright install chromium && node scripts/responsive-check.mjs"
}
```

- [ ] **Step 3: Create the Playwright script**

Create `frontend/scripts/responsive-check.mjs` with:

```js
import { spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import { chromium } from 'playwright';

const rootUrl = 'http://127.0.0.1:4173/ignixa-lab/';
const viewports = [
  { width: 1280, height: 900, label: 'desktop' },
  { width: 840, height: 900, label: 'medium' },
  { width: 680, height: 900, label: 'small' },
  { width: 400, height: 900, label: 'mobile' },
];

const routes = [
  {
    path: 'conformance.html',
    label: 'Conformance',
    checks: async (page) => {
      await page.getByRole('button', { name: 'Setup' }).waitFor();
      await page.getByRole('button', { name: 'Runner' }).click();
      await assertNoHorizontalOverflow(page, 'Conformance Runner');
      await page.getByRole('button', { name: 'Report' }).click();
      await assertNoHorizontalOverflow(page, 'Conformance Report');
    },
  },
  {
    path: 'lab.html',
    label: 'Benches',
    checks: async (page) => {
      await page.getByRole('tab', { name: 'FHIRPath' }).waitFor();
      await assertNoHorizontalOverflow(page, 'Benches FHIRPath');
      await page.getByRole('tab', { name: 'Validation' }).click();
      await assertNoHorizontalOverflow(page, 'Benches Validation');
      await page.getByRole('tab', { name: 'Fakes' }).click();
      await assertNoHorizontalOverflow(page, 'Benches Fakes');
    },
  },
];

const server = spawn('npm', ['run', 'preview', '--', '--host', '127.0.0.1'], {
  shell: true,
  stdio: ['ignore', 'pipe', 'pipe'],
});

let serverOutput = '';
server.stdout.on('data', (chunk) => {
  serverOutput += chunk.toString();
});
server.stderr.on('data', (chunk) => {
  serverOutput += chunk.toString();
});

try {
  await waitForPreview();
  const browser = await chromium.launch();
  try {
    for (const viewport of viewports) {
      const page = await browser.newPage({ viewport });
      try {
        for (const route of routes) {
          await page.goto(`${rootUrl}${route.path}`, { waitUntil: 'networkidle' });
          await expectVisible(page.locator('header'), `${route.label} header`);
          await assertNoHorizontalOverflow(page, `${route.label} ${viewport.label}`);
          await route.checks(page);
        }
      } finally {
        await page.close();
      }
    }
  } finally {
    await browser.close();
  }
} finally {
  server.kill();
}

async function waitForPreview() {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    if (server.exitCode !== null) {
      throw new Error(`Vite preview exited early.\n${serverOutput}`);
    }
    try {
      const response = await fetch(`${rootUrl}conformance.html`);
      if (response.ok) {
        return;
      }
    } catch {
      // Preview is still starting.
    }
    await delay(250);
  }
  throw new Error(`Timed out waiting for Vite preview.\n${serverOutput}`);
}

async function assertNoHorizontalOverflow(page, label) {
  const overflow = await page.evaluate(() => {
    const documentElement = document.documentElement;
    return {
      clientWidth: documentElement.clientWidth,
      scrollWidth: documentElement.scrollWidth,
    };
  });
  if (overflow.scrollWidth > overflow.clientWidth + 1) {
    throw new Error(
      `${label} has horizontal overflow: scrollWidth ${overflow.scrollWidth}, clientWidth ${overflow.clientWidth}`,
    );
  }
}

async function expectVisible(locator, label) {
  if (!(await locator.isVisible())) {
    throw new Error(`${label} is not visible`);
  }
}
```

- [ ] **Step 4: Run the new check to capture the baseline failure**

Run:

```powershell
Set-Location frontend
npm run build
npm run responsive:check
```

Expected before layout fixes: build succeeds, and `responsive:check` fails if any current viewport has horizontal overflow. Record the failing route and viewport from the thrown error.

- [ ] **Step 5: Commit**

Run:

```powershell
git add frontend/package.json frontend/package-lock.json frontend/scripts/responsive-check.mjs
git commit -m "Add responsive Playwright smoke check" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 2: Repair Conformance responsive CSS

**Files:**
- Modify: `frontend/src/App.css`

- [ ] **Step 1: Add medium-width top bar and runner wrapping**

In `frontend/src/App.css`, before the existing `@media (max-width: 640px)` block, add:

```css
@media (max-width: 900px) {
  .top-bar {
    align-items: flex-start;
  }

  .top-bar__brand,
  .top-bar__nav,
  .top-bar__readout,
  .top-bar__run-button {
    min-width: 0;
  }

  .top-bar__spacer {
    flex: 1 1 auto;
  }

  .runner-status {
    flex-wrap: wrap;
    row-gap: 8px;
  }

  .runner-status__bar {
    flex: 1 1 220px;
  }

  .runner-status__spacer {
    display: none;
  }

  .report-header__meta {
    overflow-wrap: anywhere;
  }
}
```

- [ ] **Step 2: Add tablet-width content wrapping**

Immediately after the 900px block, add:

```css
@media (max-width: 760px) {
  .setup-screen,
  .report-screen {
    padding-inline: 16px;
  }

  .suite-row {
    flex-wrap: wrap;
    row-gap: 6px;
  }

  .suite-row__name {
    max-width: calc(100% - 42px);
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .suite-row__summary {
    flex-basis: 100%;
    padding-left: 30px;
  }

  .suite-row__fixture-link {
    margin-left: 30px;
  }

  .test-list__group-header {
    flex-wrap: wrap;
  }

  .test-script__link {
    margin-left: 0;
    flex-basis: 100%;
  }

  .report-header {
    gap: 14px;
  }

  .report-header__spacer {
    display: none;
  }

  .report-header__meta {
    flex-basis: 100%;
  }

  .suite-bar__header {
    flex-wrap: wrap;
  }

  .suite-bar__detail {
    flex-basis: 100%;
    order: 3;
  }
}
```

- [ ] **Step 3: Replace the existing mobile block with a stronger mobile block**

Keep the existing comment and `@media (max-width: 640px)` selector, but make its body:

```css
@media (max-width: 640px) {
  .top-bar {
    padding: 10px 14px;
    gap: 10px;
  }

  .top-bar__brand {
    flex: 1 1 auto;
  }

  .top-bar__nav {
    order: 4;
    flex: 1 1 100%;
    margin-left: 0;
    overflow-x: auto;
  }

  .top-bar__nav-item {
    flex: 1 0 auto;
    padding: 6px 10px;
  }

  .top-bar__readout {
    display: none;
  }

  .top-bar__run-button {
    padding-inline: 12px;
  }

  .setup-screen {
    padding: 24px 12px 44px;
  }

  .setup-panel {
    padding: 15px 14px;
  }

  .endpoint-input {
    flex-wrap: wrap;
  }

  .endpoint-input__prefix {
    flex: 0 0 auto;
  }

  .endpoint-input__field {
    min-width: 0;
  }

  .endpoint-input__run {
    flex: 0 0 42px;
  }

  .suite-picker__header {
    flex-wrap: wrap;
    row-gap: 8px;
  }

  .suite-picker__header .setup-panel__title {
    flex-basis: 100%;
  }

  .setup-screen__start-button {
    width: 100%;
  }

  .runner-screen__body {
    grid-template-columns: 1fr;
  }

  .suite-tree {
    border-right: none;
    border-bottom: 1px solid var(--border);
    max-height: 260px;
    overflow: auto;
  }

  .test-list {
    padding: 12px 14px 32px;
  }

  .test-list__toolbar {
    flex-wrap: wrap;
    row-gap: 8px;
  }

  .test-list__search {
    flex: 1 1 100%;
    max-width: none;
  }

  .test-list__spacer {
    display: none;
  }

  .test-row__header,
  .step__header {
    align-items: flex-start;
  }

  .test-row__tabs {
    overflow-x: auto;
  }

  .test-row__tab {
    flex: 1 0 auto;
  }

  .assertion__header {
    flex-wrap: wrap;
    row-gap: 4px;
  }

  .assertion__meta {
    flex-basis: 100%;
  }

  .assertion__diff-grid {
    grid-template-columns: 1fr;
  }

  .report-screen {
    padding: 16px 12px 44px;
  }

  .report-header {
    align-items: stretch;
  }

  .report-header__divider {
    display: none;
  }

  .report-header__pills {
    flex-wrap: wrap;
  }

  .report-header__view-failing,
  .report-header__download {
    flex: 1 1 180px;
  }

  .report-panel {
    padding: 14px;
  }

  .report-panel__header {
    flex-direction: column;
    gap: 4px;
  }

  .coverage-map {
    min-width: 720px;
  }

  .report-panel:has(.coverage-map) {
    overflow-x: auto;
  }
}
```

- [ ] **Step 4: Run the responsive check for Conformance**

Run:

```powershell
Set-Location frontend
npm run build
npm run responsive:check
```

Expected: Conformance routes no longer throw horizontal-overflow errors. Benches may still fail until Task 3.

- [ ] **Step 5: Commit**

Run:

```powershell
git add frontend/src/App.css
git commit -m "Fix conformance responsive layouts" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 3: Repair Benches responsive primitives and top bar

**Files:**
- Modify: `frontend/src/benches/components/styles.ts`
- Modify: `frontend/src/benches/components/primitives.tsx`
- Modify: `frontend/src/benches/BenchesApp.tsx`

- [ ] **Step 1: Make bench pill groups shrink safely**

In `frontend/src/benches/components/styles.ts`, update `pillGroupStyle`:

```ts
export const pillGroupStyle: CSSProperties = {
  display: 'flex',
  flexWrap: 'wrap',
  gap: 2,
  background: 'var(--inset)',
  borderRadius: 8,
  padding: 3,
  maxWidth: '100%',
  minWidth: 0,
};
```

Update `pillItemStyle` so each pill can shrink without widening the viewport:

```ts
export function pillItemStyle(active: boolean): CSSProperties {
  return {
    minWidth: 0,
    padding: '6px 12px',
    borderRadius: 6,
    fontSize: 12.5,
    cursor: 'pointer',
    background: active ? 'var(--pill)' : 'transparent',
    color: active ? 'var(--text)' : 'var(--text3)',
    fontWeight: active ? 600 : 500,
    boxShadow: active ? '0 1px 3px var(--border2)' : 'none',
    whiteSpace: 'nowrap',
  };
}
```

- [ ] **Step 2: Allow compact bench headers to place the spacer on its own line**

In `frontend/src/benches/components/styles.ts`, update `benchHeaderStyle`:

```ts
export function benchHeaderStyle(compact: boolean): CSSProperties {
  return {
    display: 'flex',
    alignItems: compact ? 'flex-start' : 'baseline',
    gap: compact ? 8 : 12,
    flexWrap: 'wrap',
    minWidth: 0,
  };
}
```

- [ ] **Step 3: Give Pills a shrink-safe inline style**

In `frontend/src/benches/components/primitives.tsx`, change the wrapper in `Pills`:

```tsx
<div style={{ ...pillGroupStyle, minWidth: 0 }} role="tablist">
```

- [ ] **Step 4: Update Benches top bar styles**

In `frontend/src/benches/BenchesApp.tsx`, replace `topBarStyle` with:

```ts
function topBarStyle(compact: boolean): CSSProperties {
  return {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: compact ? 8 : 14,
    padding: compact ? '10px 12px' : '12px 20px',
    background: 'var(--panel)',
    borderBottom: '1px solid var(--border)',
    position: 'sticky',
    top: 0,
    zIndex: 20,
    minWidth: 0,
  };
}
```

In the header markup, change the `Pills` call to wrap it in a shrink-safe container:

```tsx
<div style={{ order: compactHeader ? 4 : 0, flex: compactHeader ? '1 1 100%' : '0 1 auto', minWidth: 0 }}>
  <Pills items={BENCH_TABS} activeId={bench} onChange={setBench} />
</div>
```

- [ ] **Step 5: Make share/theme buttons non-shrinking**

In `BenchesApp.tsx`, add `flex: 'none'` to both share and theme button style objects:

```ts
flex: 'none',
```

- [ ] **Step 6: Run lint/build**

Run:

```powershell
Set-Location frontend
npm run lint
npm run build
```

Expected: both commands pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add frontend/src/benches/components/styles.ts frontend/src/benches/components/primitives.tsx frontend/src/benches/BenchesApp.tsx
git commit -m "Fix benches responsive shell" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 4: Repair per-bench compact controls

**Files:**
- Modify: `frontend/src/benches/fhirpath/FhirPathBench.tsx`
- Modify: `frontend/src/benches/validation/ValidationBench.tsx`
- Modify: `frontend/src/benches/fakes/FakesBench.tsx`

- [ ] **Step 1: Reduce FHIRPath AST indentation on compact screens**

In `frontend/src/benches/fhirpath/FhirPathBench.tsx`, update `AstRows` props:

```tsx
function AstRows({
  node,
  depth,
  compact,
  onNodeClick,
}: {
  node: FpAstNode;
  depth: number;
  compact: boolean;
  onNodeClick: (node: FpAstNode) => void;
}) {
```

Change the padding expression:

```ts
padding: `3px 0 3px ${depth * (compact ? 10 : 18) + 2}px`,
```

Pass `compact` through recursive rows:

```tsx
<AstRows key={index} node={child} depth={depth + 1} compact={compact} onNodeClick={onNodeClick} />
```

Where root AST rows are rendered, change the calls to:

```tsx
? invertedAstRoots.map((node, index) => (
    <AstRows key={index} node={node} depth={0} compact={compact} onNodeClick={handleAstNodeClick} />
  ))
: <AstRows node={result.ast} depth={0} compact={compact} onNodeClick={handleAstNodeClick} />}
```

- [ ] **Step 2: Make the FHIRPath version row wrap**

In `FhirPathBench.tsx`, change:

```tsx
<div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
```

to:

```tsx
<div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap', minWidth: 0 }}>
```

- [ ] **Step 3: Let Validation control groups wrap**

In `frontend/src/benches/validation/ValidationBench.tsx`, change `controlGroupStyle`:

```ts
const controlGroupStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  flexWrap: 'wrap',
  minWidth: 0,
  maxWidth: '100%',
};
```

Change the skip terminology span style to allow wrapping:

```tsx
<span style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, color: 'var(--text3)', flex: '1 1 180px', minWidth: 0 }}>
```

- [ ] **Step 4: Make Fakes delivery controls shrink safely**

In `frontend/src/benches/fakes/FakesBench.tsx`, update `deliveryBarStyle`:

```ts
const deliveryBarStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 12,
  flexWrap: 'wrap',
  background: 'var(--panel)',
  border: '1px solid var(--border)',
  borderRadius: 12,
  padding: '9px 12px',
  minWidth: 0,
};
```

Change `barDividerStyle` so it can disappear naturally in wrapped rows:

```ts
const barDividerStyle: CSSProperties = { width: 1, height: 22, background: 'var(--border2)', flex: '0 0 auto' };
```

- [ ] **Step 5: Run Playwright responsive check**

Run:

```powershell
Set-Location frontend
npm run build
npm run responsive:check
```

Expected: all Conformance and Benches viewport checks pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add frontend/src/benches/fhirpath/FhirPathBench.tsx frontend/src/benches/validation/ValidationBench.tsx frontend/src/benches/fakes/FakesBench.tsx
git commit -m "Fix bench compact controls" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 5: Final verification and cleanup

**Files:**
- Inspect: git status and generated artifacts
- Modify only if checks reveal a concrete issue

- [ ] **Step 1: Run frontend lint**

Run:

```powershell
Set-Location frontend
npm run lint
```

Expected: exits 0.

- [ ] **Step 2: Run frontend build**

Run:

```powershell
Set-Location frontend
npm run build
```

Expected: exits 0 and writes `frontend/dist`.

- [ ] **Step 3: Run Playwright responsive verification**

Run:

```powershell
Set-Location frontend
npm run responsive:check
```

Expected: exits 0 with no horizontal-overflow errors.

- [ ] **Step 4: Check git status**

Run:

```powershell
git --no-pager status --short
```

Expected: only intentional committed changes remain; `.superpowers/` may be untracked from the brainstorming companion and should not be committed.

- [ ] **Step 5: Commit any final fixes**

If Task 5 required additional code changes, commit them:

```powershell
git add <changed-files>
git commit -m "Verify responsive viewport fixes" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```
