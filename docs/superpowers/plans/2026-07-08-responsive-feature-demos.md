# Responsive Feature Demos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the static landing page responsive and replace the FHIRPath-only hero card with a rotating multi-feature demo widget.

**Architecture:** Keep `frontend/index.html` as a dependency-free static Vite landing page. Centralize landing layout and demo styling in the existing inline `<style>` block, use a small inline script for demo rotation, and extend the Playwright responsive smoke check so the root landing page is covered alongside Conformance and Benches.

**Tech Stack:** Static HTML, CSS media queries, vanilla JavaScript, Vite, Playwright, npm scripts.

---

## File structure

- Modify `frontend/scripts/responsive-check.mjs` to add the root landing page route, assert the new demo controls exist, verify manual selection works, and verify reduced-motion disables rotation.
- Modify `frontend/index.html` only for landing page layout, styling, markup, and the dependency-free demo rotation script.
- No package changes are required because Playwright and `responsive:check` already exist.
- No React app files are required because the root landing page is intentionally static and separate from `conformance.html` and `lab.html`.

## Task 1: Add failing landing-page responsive checks

**Files:**
- Modify: `frontend/scripts/responsive-check.mjs`

- [ ] **Step 1: Add a Landing route before the Conformance route**

In `frontend/scripts/responsive-check.mjs`, replace the start of the `routes` array with this version so the first route covers `index.html`:

```js
const routes = [
  {
    path: '',
    label: 'Landing',
    checks: async (page) => {
      await expectVisible(page.locator('.ix-hero-demo'), 'Landing rotating demo');
      await expectCount(page.locator('.ix-demo-tab'), 'Landing demo feature controls', 6);
      await page.getByRole('button', { name: 'Validation demo' }).click();
      await expectVisible(page.getByText('validation · Patient'), 'Landing validation demo title');
      await assertNoHorizontalOverflow(page, 'Landing selected Validation demo');
      await assertReducedMotionStopsAutoRotation(page);
    },
  },
  {
    path: 'conformance.html',
    label: 'Conformance',
    checks: async (page) => {
```

- [ ] **Step 2: Add the reduced-motion helper**

In `frontend/scripts/responsive-check.mjs`, add this helper after `expectCount`:

```js
async function assertReducedMotionStopsAutoRotation(page) {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.reload({ waitUntil: 'networkidle' });
  await expectVisible(page.locator('.ix-hero-demo'), 'Landing reduced-motion demo');
  const initialDemo = await page.locator('.ix-hero-demo').getAttribute('data-active-demo');
  await page.waitForTimeout(5200);
  const laterDemo = await page.locator('.ix-hero-demo').getAttribute('data-active-demo');
  if (initialDemo !== laterDemo) {
    throw new Error(`Landing demo rotated despite reduced motion: ${initialDemo} -> ${laterDemo}`);
  }
  await page.emulateMedia({ reducedMotion: 'no-preference' });
}
```

- [ ] **Step 3: Run the responsive check to verify it fails for the missing landing demo**

Run:

```powershell
Set-Location 'C:\Users\bkowitz\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-friendly-adventure\frontend'
npm run responsive:check
```

Expected: the script fails on `Landing rotating demo is not visible` because `.ix-hero-demo` does not exist yet. If it fails earlier because of current horizontal overflow on the landing page, keep that failure too; Task 2 must fix both the missing demo and overflow.

## Task 2: Convert landing layout to shrink-safe classes

**Files:**
- Modify: `frontend/index.html:18-299`

- [ ] **Step 1: Replace the landing style block with class-based responsive CSS**

In `frontend/index.html`, replace the entire `<style>` block with this CSS. Keep it inside the existing `<head>`.

```css
html,
body {
  margin: 0;
  padding: 0;
}

*,
*::before,
*::after {
  box-sizing: border-box;
}

body {
  min-width: 0;
  font-family: 'IBM Plex Sans', system-ui, sans-serif;
  background: #161318;
  color: #e7e4ec;
  overflow-x: hidden;
}

a {
  text-decoration: none;
  color: inherit;
}

button {
  border: 0;
  font: inherit;
}

@keyframes ixblink {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.25;
  }
}

.ix-page {
  min-height: 100vh;
  overflow-x: clip;
}

.ix-topbar {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 15px clamp(18px, 4vw, 56px);
  border-bottom: 1px solid rgba(231, 228, 236, 0.08);
  background: #1a1620;
}

.ix-brand {
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
}

.ix-logo {
  width: 34px;
  height: 34px;
  border-radius: 8px;
  background: linear-gradient(90deg, #a855f7, #d6336c);
  flex: none;
}

.ix-brand-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.ix-brand-name {
  font-size: 14.5px;
  font-weight: 700;
  letter-spacing: -0.01em;
}

.ix-brand-kicker,
.ix-kicker,
.ix-section-label,
.ix-demo-eyebrow,
.ix-demo-result-label,
.ix-tool-kicker {
  font-family: 'IBM Plex Mono', monospace;
  text-transform: uppercase;
}

.ix-brand-kicker {
  font-size: 9px;
  letter-spacing: 0.16em;
  color: #8b8494;
}

.ix-nav-link {
  font-size: 13px;
  color: #c9c3d4;
}

.ix-nav-link:hover {
  color: #e7e4ec;
}

.ix-switch {
  display: flex;
  gap: 3px;
  padding: 3px;
  border: 1px solid rgba(231, 228, 236, 0.14);
  border-radius: 10px;
  min-width: 0;
}

.ix-switch-item {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  padding: 6px 12px;
  border-radius: 7px;
  font-size: 12.5px;
  font-weight: 500;
  color: #c9c3d4;
  white-space: nowrap;
}

.ix-switch-item:hover {
  background: #241f2d;
  color: #e7e4ec;
}

.ix-spacer {
  flex: 1 1 auto;
}

.ix-hero {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(320px, 1fr);
  gap: clamp(28px, 5vw, 44px);
  padding: clamp(46px, 7vw, 64px) clamp(18px, 5vw, 56px) clamp(40px, 5vw, 52px);
  align-items: center;
}

.ix-hero-copy {
  display: flex;
  flex-direction: column;
  gap: 22px;
  min-width: 0;
}

.ix-kicker {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.18em;
  color: #a78bfa;
}

.ix-hero-title {
  margin: 0;
  font-size: clamp(38px, 7vw, 52px);
  line-height: 1.04;
  font-weight: 700;
  letter-spacing: -0.03em;
  text-wrap: balance;
}

.ix-hero-body {
  margin: 0;
  max-width: 480px;
  font-size: clamp(15.5px, 2vw, 16.5px);
  line-height: 1.6;
  color: #c9c3d4;
}

.ix-actions {
  display: flex;
  align-items: center;
  gap: 14px;
  flex-wrap: wrap;
  margin-top: 6px;
}

.ix-cta,
.ix-cta-secondary {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 9px;
  font-size: 14.5px;
  font-weight: 600;
  text-align: center;
}

.ix-cta {
  padding: 12px 22px;
  background: #7c3aed;
  color: #fff;
  box-shadow: 0 4px 16px rgba(124, 58, 237, 0.4);
}

.ix-cta-secondary {
  padding: 12px 20px;
  border: 1px solid rgba(231, 228, 236, 0.16);
  color: #e7e4ec;
}

.ix-cta-secondary:hover {
  border-color: rgba(231, 228, 236, 0.32);
}

.ix-stats,
.ix-section-heading,
.ix-tool-grid,
.ix-footer {
  margin-inline: clamp(18px, 5vw, 56px);
}

.ix-stats {
  padding-bottom: 40px;
}

.ix-stats-inner {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px 20px;
  padding: 14px 2px;
  border-top: 1px solid rgba(231, 228, 236, 0.07);
  border-bottom: 1px solid rgba(231, 228, 236, 0.07);
  font-family: 'IBM Plex Mono', monospace;
  font-size: 12.5px;
  color: #8b8494;
}

.ix-stat-strong {
  color: #a78bfa;
  font-weight: 600;
}

.ix-dot {
  opacity: 0.4;
}

.ix-section-heading {
  padding-bottom: 20px;
}

.ix-section-label {
  font-size: 10.5px;
  font-weight: 600;
  letter-spacing: 0.16em;
  color: #8b8494;
}

.ix-tool-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20px;
  padding-bottom: 60px;
}

.ix-tool-link {
  min-width: 0;
}

.ix-tool-card {
  min-width: 0;
  height: 100%;
  border-radius: 14px;
  background: #1d1922;
  border: 1px solid rgba(231, 228, 236, 0.09);
  padding: 24px 24px 22px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  transition: border-color 0.15s ease;
}

.ix-tool-card:hover {
  border-color: rgba(167, 139, 250, 0.5);
}

.ix-tool-header {
  display: flex;
  align-items: center;
  gap: 11px;
  min-width: 0;
}

.ix-tool-icon {
  width: 38px;
  height: 38px;
  border-radius: 10px;
  display: grid;
  place-items: center;
  flex: none;
  font-size: 18px;
}

.ix-tool-icon--violet {
  background: rgba(124, 58, 237, 0.16);
}

.ix-tool-icon--teal {
  background: rgba(20, 184, 166, 0.16);
}

.ix-tool-title-block {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.ix-tool-title {
  font-size: 16px;
  font-weight: 700;
}

.ix-tool-kicker {
  font-size: 9.5px;
  letter-spacing: 0.14em;
  color: #8b8494;
}

.ix-tool-description {
  margin: 0;
  font-size: 13.5px;
  line-height: 1.6;
  color: #c9c3d4;
}

.ix-tool-action {
  margin-top: auto;
  font-size: 13px;
  font-weight: 600;
  color: #a78bfa;
}

.ix-tool-action--teal {
  color: #5eead4;
}

.ix-footer {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 20px 0;
  border-top: 1px solid rgba(231, 228, 236, 0.07);
  font-size: 12px;
  color: #8b8494;
}

.ix-footer-link {
  color: #8b8494;
}

.ix-footer-link:hover {
  color: #c9c3d4;
}

@media (max-width: 860px) {
  .ix-topbar {
    flex-wrap: wrap;
  }

  .ix-switch {
    order: 3;
    flex: 1 1 100%;
    overflow-x: auto;
  }

  .ix-switch-item {
    flex: 1 0 auto;
  }

  .ix-hero {
    grid-template-columns: minmax(0, 1fr);
    align-items: start;
  }

  .ix-tool-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 520px) {
  .ix-actions,
  .ix-cta,
  .ix-cta-secondary {
    width: 100%;
  }

  .ix-stats-inner {
    align-items: flex-start;
    flex-direction: column;
  }

  .ix-dot {
    display: none;
  }

  .ix-footer {
    flex-wrap: wrap;
  }

  .ix-footer .ix-spacer {
    flex-basis: 100%;
  }
}
```

- [ ] **Step 2: Replace top bar markup with class-based markup**

Replace the current top bar `<div ...>` block with:

```html
<div class="ix-page">
  <header class="ix-topbar">
    <a class="ix-brand" href="./" aria-label="Ignixa home">
      <span class="ix-logo" aria-hidden="true"></span>
      <span class="ix-brand-text">
        <span class="ix-brand-name">Ignixa</span>
        <span class="ix-brand-kicker">FHIR Toolkit</span>
      </span>
    </a>
    <nav class="ix-switch" aria-label="Ignixa tools">
      <a class="ix-switch-item" href="./conformance.html"><span aria-hidden="true">▶</span>Conformance</a>
      <a class="ix-switch-item" href="./lab.html"><span aria-hidden="true">ƒ</span>Benches</a>
    </nav>
    <div class="ix-spacer"></div>
    <a class="ix-nav-link" href="https://github.com/brendankowitz/ignixa-lab">GitHub</a>
  </header>
```

Keep the closing `</div>` for `.ix-page` until after the footer in Step 4.

- [ ] **Step 3: Replace the hero copy wrapper with responsive class markup**

Replace the current hero opening `<div style="display:grid...">` and hero copy column through the CTA row with:

```html
  <main>
    <section class="ix-hero" aria-labelledby="ix-hero-title">
      <div class="ix-hero-copy">
        <span class="ix-kicker">Open source · R4 · R4B · R5 · R6 · STU3</span>
        <h1 id="ix-hero-title" class="ix-hero-title">Ignixa FHIR&nbsp;toolkit.</h1>
        <p class="ix-hero-body">
          An open toolkit over one FHIR engine: evaluate FHIRPath, validate resources, model FML and SQL-on-FHIR
          expressions, generate synthetic test data — from single resources to full clinical scenarios and workflows —
          and run conformance TestScripts against real servers.
        </p>
        <div class="ix-actions">
          <a class="ix-cta" href="./conformance.html">Start a conformance run →</a>
          <a class="ix-cta-secondary" href="./lab.html">Try the benches</a>
        </div>
      </div>
```

Task 3 replaces the old hero code card immediately after this block.

- [ ] **Step 4: Replace stats, launcher, and footer markup with responsive class markup**

After Task 3's demo card, replace the remaining stats strip, tool launcher, and footer with:

```html
    </section>

    <section class="ix-stats" aria-label="Ignixa toolkit facts">
      <div class="ix-stats-inner">
        <span><span class="ix-stat-strong">87</span> bundled conformance TestScripts</span>
        <span class="ix-dot">·</span>
        <span><span class="ix-stat-strong">5</span> benches · FHIRPath, Validation, FML, SQL-on-FHIR, Fakes</span>
        <span class="ix-dot">·</span>
        <span><span class="ix-stat-strong">5</span> FHIR releases · R4, R4B, R5, R6, STU3</span>
        <span class="ix-dot">·</span>
        <span>MIT licensed</span>
      </div>
    </section>

    <section class="ix-section-heading" aria-labelledby="ix-entry-title">
      <span id="ix-entry-title" class="ix-section-label">Two ways in</span>
    </section>

    <section class="ix-tool-grid" aria-label="Ignixa entry points">
      <a class="ix-tool-link" href="./conformance.html">
        <article class="ix-tool-card">
          <div class="ix-tool-header">
            <div class="ix-tool-icon ix-tool-icon--violet" aria-hidden="true">▶</div>
            <div class="ix-tool-title-block">
              <span class="ix-tool-title">Conformance Testing</span>
              <span class="ix-tool-kicker">Conformance runner</span>
            </div>
          </div>
          <p class="ix-tool-description">
            Point it at a FHIR server, choose suites, and run thousands of conformance tests. Get a shareable pass/fail
            report with every failure traced to its assertion.
          </p>
          <span class="ix-tool-action">Configure a run →</span>
        </article>
      </a>
      <a class="ix-tool-link" href="./lab.html">
        <article class="ix-tool-card">
          <div class="ix-tool-header">
            <div class="ix-tool-icon ix-tool-icon--teal" aria-hidden="true">⌘</div>
            <div class="ix-tool-title-block">
              <span class="ix-tool-title">Expression Benches</span>
              <span class="ix-tool-kicker">FHIRPath · FML · SQL-on-FHIR · Fakes</span>
            </div>
          </div>
          <p class="ix-tool-description">
            An interactive playground for the engine. Write an expression, watch it parse into an AST, evaluate it live
            against sample resources, and generate synthetic test data — populations, clinical scenarios, and workflow
            packs — with the Fakes bench.
          </p>
          <span class="ix-tool-action ix-tool-action--teal">Open the benches →</span>
        </article>
      </a>
    </section>
  </main>

  <footer class="ix-footer">
    <span>© 2026 Ignixa</span>
    <span class="ix-dot">·</span>
    <span>MIT License</span>
    <div class="ix-spacer"></div>
    <a class="ix-footer-link" href="https://github.com/brendankowitz/ignixa-lab">GitHub</a>
  </footer>
</div>
```

- [ ] **Step 5: Run the failing responsive check again**

Run:

```powershell
Set-Location 'C:\Users\bkowitz\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-friendly-adventure\frontend'
npm run responsive:check
```

Expected: horizontal overflow from the static layout is fixed or reduced, but the check still fails because Task 3 has not added `.ix-hero-demo` and `.ix-demo-tab`.

## Task 3: Add the rotating multi-feature hero demo

**Files:**
- Modify: `frontend/index.html`

- [ ] **Step 1: Add demo widget CSS**

In the same `<style>` block, add this CSS before the first `@media` block:

```css
.ix-hero-demo {
  min-width: 0;
  border-radius: 14px;
  background: #131017;
  border: 1px solid rgba(231, 228, 236, 0.09);
  overflow: hidden;
  box-shadow: 0 20px 50px -20px rgba(0, 0, 0, 0.6);
}

.ix-demo-chrome {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 11px 14px;
  border-bottom: 1px solid rgba(231, 228, 236, 0.07);
  background: #1a1620;
}

.ix-demo-dot {
  width: 10px;
  height: 10px;
  border-radius: 99px;
  flex: none;
}

.ix-demo-dot--red {
  background: #f87171;
}

.ix-demo-dot--yellow {
  background: #fcd34d;
}

.ix-demo-dot--green {
  background: #4ade80;
}

.ix-demo-title {
  min-width: 0;
  overflow: hidden;
  margin-left: 8px;
  font-family: 'IBM Plex Mono', monospace;
  font-size: 10.5px;
  color: #8b8494;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ix-demo-body {
  display: grid;
  gap: 16px;
  padding: 18px;
  font-family: 'IBM Plex Mono', monospace;
  font-size: 13px;
  line-height: 1.7;
}

.ix-demo-eyebrow,
.ix-demo-result-label {
  font-size: 10px;
  letter-spacing: 0.14em;
  color: #8b8494;
}

.ix-demo-code {
  min-width: 0;
  margin: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  color: #e7e4ec;
}

.ix-demo-tabs,
.ix-demo-chips {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.ix-demo-tab {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
  padding: 5px 9px;
  background: rgba(231, 228, 236, 0.06);
  color: #c9c3d4;
  cursor: pointer;
}

.ix-demo-tab[aria-pressed='true'] {
  background: rgba(167, 139, 250, 0.2);
  color: #c4b5fd;
}

.ix-demo-chip {
  border-radius: 6px;
  padding: 3px 8px;
  font-size: 10.5px;
  background: rgba(20, 184, 166, 0.15);
  color: #5eead4;
}

.ix-demo-chip--pink {
  background: rgba(236, 72, 153, 0.15);
  color: #f9a8d4;
}

.ix-demo-chip--amber {
  background: rgba(245, 158, 11, 0.15);
  color: #fcd34d;
}

.ix-demo-result {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.ix-demo-status-dot {
  width: 7px;
  height: 7px;
  border-radius: 99px;
  background: #4ade80;
  flex: none;
}

.ix-demo-result-text {
  min-width: 0;
  overflow-wrap: anywhere;
  color: #e7e4ec;
}

.ix-demo-cursor {
  animation: ixblink 1.1s steps(1) infinite;
  color: #a78bfa;
}

@media (prefers-reduced-motion: reduce) {
  .ix-demo-cursor {
    animation: none;
  }
}
```

- [ ] **Step 2: Replace the old hero code card with demo widget markup**

Replace the old `<!-- hero code card -->` block with:

```html
      <section class="ix-hero-demo" data-active-demo="fhirpath" aria-label="Rotating feature demo">
        <div class="ix-demo-chrome">
          <span class="ix-demo-dot ix-demo-dot--red" aria-hidden="true"></span>
          <span class="ix-demo-dot ix-demo-dot--yellow" aria-hidden="true"></span>
          <span class="ix-demo-dot ix-demo-dot--green" aria-hidden="true"></span>
          <span class="ix-demo-title">fhirpath · Patient</span>
        </div>
        <div class="ix-demo-body">
          <div class="ix-demo-tabs" role="group" aria-label="Feature demos">
            <button class="ix-demo-tab" type="button" data-demo-tab="fhirpath" aria-pressed="true">FHIRPath</button>
            <button class="ix-demo-tab" type="button" data-demo-tab="fakes" aria-pressed="false">Fakes</button>
            <button class="ix-demo-tab" type="button" data-demo-tab="validation" aria-pressed="false">Validation</button>
            <button class="ix-demo-tab" type="button" data-demo-tab="conformance" aria-pressed="false">Conformance</button>
            <button class="ix-demo-tab" type="button" data-demo-tab="sqlonfhir" aria-pressed="false">SQL-on-FHIR</button>
            <button class="ix-demo-tab" type="button" data-demo-tab="fml" aria-pressed="false">FML</button>
          </div>
          <div>
            <div class="ix-demo-eyebrow">expression</div>
            <pre class="ix-demo-code">Patient.name.where(use='official').given.first()<span class="ix-demo-cursor">▌</span></pre>
          </div>
          <div class="ix-demo-chips">
            <span class="ix-demo-chip">path</span>
            <span class="ix-demo-chip ix-demo-chip--pink">fn where</span>
            <span class="ix-demo-chip ix-demo-chip--amber">literal</span>
          </div>
          <div>
            <div class="ix-demo-result-label">result · string[1]</div>
            <div class="ix-demo-result">
              <span class="ix-demo-status-dot" aria-hidden="true"></span>
              <span class="ix-demo-result-text">"Jane"</span>
              <span class="ix-spacer"></span>
              <span aria-hidden="true">✓</span>
            </div>
          </div>
        </div>
      </section>
```

- [ ] **Step 3: Add the dependency-free rotation script**

Add this script before `</body>`:

```html
<script>
  (() => {
    const demos = [
      {
        id: 'fhirpath',
        title: 'fhirpath · Patient',
        eyebrow: 'expression',
        code: "Patient.name.where(use='official').given.first()",
        chips: [
          ['path', ''],
          ['fn where', 'ix-demo-chip--pink'],
          ['literal', 'ix-demo-chip--amber'],
        ],
        resultLabel: 'result · string[1]',
        result: '"Jane"',
      },
      {
        id: 'fakes',
        title: 'fakes · synthetic scenario',
        eyebrow: 'generate',
        code: 'Patient + Encounter + Observation\\nstate: WA · domain: cardiology',
        chips: [
          ['synthetic', ''],
          ['scenario', 'ix-demo-chip--pink'],
          ['R4', 'ix-demo-chip--amber'],
        ],
        resultLabel: 'bundle · 18 resources',
        result: 'Created coherent test data for one clinical workflow',
      },
      {
        id: 'validation',
        title: 'validation · Patient',
        eyebrow: 'validate',
        code: '{ "resourceType": "Patient", "birthDate": "1974-12-25" }',
        chips: [
          ['schema', ''],
          ['profile', 'ix-demo-chip--pink'],
          ['issue list', 'ix-demo-chip--amber'],
        ],
        resultLabel: 'result · valid',
        result: '0 errors · 1 informational warning',
      },
      {
        id: 'conformance',
        title: 'conformance · TestScript',
        eyebrow: 'run',
        code: 'GET /metadata\\nGET /Patient/example\\nassert response.status = 200',
        chips: [
          ['TestScript', ''],
          ['assertions', 'ix-demo-chip--pink'],
          ['report', 'ix-demo-chip--amber'],
        ],
        resultLabel: 'run · 87 scripts',
        result: '84 pass · 2 fail · 1 skipped',
      },
      {
        id: 'sqlonfhir',
        title: 'sqlonfhir · Observation',
        eyebrow: 'query',
        code: 'SELECT code, value, effective\\nFROM Observation\\nWHERE subject = @patient',
        chips: [
          ['SQL', ''],
          ['FHIR tables', 'ix-demo-chip--pink'],
          ['preview', 'ix-demo-chip--amber'],
        ],
        resultLabel: 'rows · 12',
        result: 'bp-systolic | 124 mmHg | 2026-07-08',
      },
      {
        id: 'fml',
        title: 'fml · map transform',
        eyebrow: 'transform',
        code: 'src.name as n -> tgt.name = n\\nsrc.dob as d -> tgt.birthDate = d',
        chips: [
          ['mapping', ''],
          ['diff', 'ix-demo-chip--pink'],
          ['target resource', 'ix-demo-chip--amber'],
        ],
        resultLabel: 'output · Patient',
        result: 'Mapped legacy fields into a FHIR Patient preview',
      },
    ];

    const demoShell = document.querySelector('.ix-hero-demo');
    if (!demoShell) {
      return;
    }

    const title = demoShell.querySelector('.ix-demo-title');
    const eyebrow = demoShell.querySelector('.ix-demo-eyebrow');
    const code = demoShell.querySelector('.ix-demo-code');
    const chips = demoShell.querySelector('.ix-demo-chips');
    const resultLabel = demoShell.querySelector('.ix-demo-result-label');
    const result = demoShell.querySelector('.ix-demo-result-text');
    const tabs = Array.from(demoShell.querySelectorAll('.ix-demo-tab'));
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let activeIndex = 0;
    let userPaused = false;
    let timer = null;

    const render = (index) => {
      const demo = demos[index];
      activeIndex = index;
      demoShell.dataset.activeDemo = demo.id;
      title.textContent = demo.title;
      eyebrow.textContent = demo.eyebrow;
      code.textContent = demo.code;
      const cursor = document.createElement('span');
      cursor.className = 'ix-demo-cursor';
      cursor.textContent = '▌';
      code.append(cursor);
      chips.replaceChildren(
        ...demo.chips.map(([label, modifier]) => {
          const chip = document.createElement('span');
          chip.className = modifier ? `ix-demo-chip ${modifier}` : 'ix-demo-chip';
          chip.textContent = label;
          return chip;
        }),
      );
      resultLabel.textContent = demo.resultLabel;
      result.textContent = demo.result;
      tabs.forEach((tab) => {
        tab.setAttribute('aria-pressed', String(tab.dataset.demoTab === demo.id));
        tab.setAttribute('aria-label', `${tab.textContent} demo`);
      });
    };

    const stopTimer = () => {
      if (timer !== null) {
        window.clearInterval(timer);
        timer = null;
      }
    };

    const startTimer = () => {
      stopTimer();
      if (userPaused || reducedMotion.matches) {
        return;
      }
      timer = window.setInterval(() => {
        render((activeIndex + 1) % demos.length);
      }, 4200);
    };

    tabs.forEach((tab) => {
      tab.addEventListener('click', () => {
        const nextIndex = demos.findIndex((demo) => demo.id === tab.dataset.demoTab);
        if (nextIndex === -1) {
          return;
        }
        userPaused = true;
        stopTimer();
        render(nextIndex);
      });
    });

    reducedMotion.addEventListener('change', startTimer);
    render(0);
    startTimer();
  })();
</script>
```

- [ ] **Step 4: Run the responsive check**

Run:

```powershell
Set-Location 'C:\Users\bkowitz\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-friendly-adventure\frontend'
npm run responsive:check
```

Expected: all Landing, Conformance, and Benches responsive checks pass. The Landing checks must find six demo tabs, manually select Validation, and confirm reduced-motion prevents automatic rotation.

- [ ] **Step 5: Commit**

Run:

```powershell
Set-Location 'C:\Users\bkowitz\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-friendly-adventure'
git add frontend\index.html frontend\scripts\responsive-check.mjs
git commit -m "Add responsive landing feature demos" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

## Task 4: Final landing verification and cleanup

**Files:**
- Verify: `frontend/index.html`
- Verify: `frontend/scripts/responsive-check.mjs`

- [ ] **Step 1: Run frontend lint**

Run:

```powershell
Set-Location 'C:\Users\bkowitz\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-friendly-adventure\frontend'
npm run lint
```

Expected: lint exits with code 0.

- [ ] **Step 2: Run frontend build**

Run:

```powershell
Set-Location 'C:\Users\bkowitz\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-friendly-adventure\frontend'
npm run build
```

Expected: TypeScript and Vite build exit with code 0.

- [ ] **Step 3: Run the full responsive check**

Run:

```powershell
Set-Location 'C:\Users\bkowitz\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-friendly-adventure\frontend'
npm run responsive:check
```

Expected: the script exits with code 0 for desktop, medium, small, and mobile viewports.

- [ ] **Step 4: Inspect the final diff for accidental scope creep**

Run:

```powershell
Set-Location 'C:\Users\bkowitz\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-friendly-adventure'
git --no-pager diff --stat HEAD
git --no-pager diff HEAD -- frontend\index.html frontend\scripts\responsive-check.mjs
```

Expected: only landing-page markup/style/script and responsive-check landing coverage changed after the Task 3 commit. If any unrelated files changed, do not stage them.

- [ ] **Step 5: Commit any verification-only adjustments**

Only run this if Step 4 shows small fixes made after Task 3:

```powershell
Set-Location 'C:\Users\bkowitz\.copilot\repos\copilot-worktrees\ignixa-lab\brendankowitz-friendly-adventure'
git add frontend\index.html frontend\scripts\responsive-check.mjs
git commit -m "Tighten landing responsive verification" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

Expected: no commit is needed if Task 3 already passed lint, build, and responsive checks.

## Self-review notes

- Spec coverage: Task 1 covers verification, Task 2 covers responsive landing classes and shrink-safe layout, Task 3 covers the rotating feature demo, reduced-motion behavior, keyboard/pointer controls, and static dependency-free data, and Task 4 covers final lint/build/responsive verification.
- Scope check: this plan touches only the static landing page and responsive smoke harness; it does not redesign Conformance, Benches, routing, backend behavior, or dependencies.
- Type consistency: all new test selectors match the planned landing markup: `.ix-hero-demo`, `.ix-demo-tab`, `data-active-demo`, and accessible button names like `Validation demo`.
