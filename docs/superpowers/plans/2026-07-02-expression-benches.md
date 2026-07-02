# Ignixa Expression Benches Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a second frontend page, Expression Benches, with a real FHIRPath bench (wired to the already-ported `/api/$fhirpath-*` backend) and two interactive client-side-mocked benches (FML, SQL on FHIR), per `docs/superpowers/specs/2026-07-02-expression-benches-design.md`.

**Architecture:** A second Vite HTML entry (`frontend/benches.html` → `benches-main.tsx` → `BenchesApp`) mounted alongside the existing conformance-runner SPA, sharing the same theme tokens/hook. No router, no new dependencies. The FHIRPath bench calls the real backend with a debounced auto-run; FML and SQL on FHIR port the mockup's own client-side interpreter engines with an explicit "Run" button.

**Tech Stack:** React 19 + TypeScript (existing frontend stack), Vite multi-page build, no new dependencies.

## Global Constraints

- **Backwards compatibility (hard constraint):** Do not modify `backend/src/Ignixa.Lab.Functions/Functions/FhirPathFunctions.cs`, `Services/FhirPath/ResultFormatter.cs`, or `Serialization/JsonAstVisitor.cs` in this plan. This work is frontend-only; it reads the existing contract, documented in `E:\data\src\fhirpath-lab\server-api.md`, and must not require any backend change.
- **No new dependencies:** no router, no test framework — `npm run build` (`tsc -b && vite build`) and `npm run lint` (`oxlint`) are the only automated gates, matching the existing project convention.
- **Styling:** port the mockup's own inline `style={{...}}` JSX objects (using `var(--token)` CSS custom properties from `theme/variables.ts`), not the conformance tool's hand-authored BEM CSS classes — approved as the lower-risk, lower-effort path for this feature.
- **TS strictness:** `frontend/tsconfig.app.json` has `verbatimModuleSyntax: true`, `noUnusedLocals: true`, `noUnusedParameters: true`, `erasableSyntaxOnly: true` — use `import type { ... }` for type-only imports, no enums, no unused locals/params.
- **FHIR version routes:** `stu3` → `$fhirpath-stu3`, `r4` → `$fhirpath-r4`, `r4b` → `$fhirpath-r4b`, `r5` → `$fhirpath-r5`, `r6` → `$fhirpath-r6`, all under the `/api` prefix (Vite dev proxy already forwards `/api/*` to `localhost:7071`).
- **Debounce:** FHIRPath bench auto-runs 450ms after the last edit to expression/context/resource/variables/version; in-flight requests are aborted when a new one starts. FML/SoF benches use an explicit "▶ Run" button (no auto-run), consumed once on mount with default fixtures.

---

## Task 1: Foundations — theme tokens, shared primitives, multi-page scaffold, BenchesApp shell

**Files:**
- Modify: `frontend/src/theme/variables.ts`
- Create: `frontend/src/benches/components/styles.ts`
- Create: `frontend/src/benches/components/primitives.tsx`
- Create: `frontend/src/benches/fhirpath/FhirPathBench.tsx` (placeholder, replaced in Task 3)
- Create: `frontend/src/benches/fml/FmlBench.tsx` (placeholder, replaced in Task 5)
- Create: `frontend/src/benches/sof/SofBench.tsx` (placeholder, replaced in Task 6)
- Create: `frontend/src/benches/BenchesApp.tsx`
- Create: `frontend/benches.html`
- Create: `frontend/src/benches-main.tsx`
- Modify: `frontend/vite.config.ts`
- Modify: `frontend/src/components/TopBar.tsx`

**Interfaces:**
- Produces: `Card({children, style?})`, `ErrorBanner({message})`, `Pills({items, activeId, onChange})`, `PillItem = {id: string; label: string}` from `benches/components/primitives.tsx`; style constants/functions `cardStyle`, `sectionLabelStyle`, `monoTextareaStyle`, `errorBannerStyle`, `pillGroupStyle`, `pillItemStyle(active)`, `chipStyle(bg, fg)`, `primaryButtonStyle`, `monoFont` from `benches/components/styles.ts`. New CSS custom properties on `THEME_VARIABLES`: `--pill`, `--chip-vio-bg/-fg`, `--chip-teal-bg/-fg`, `--chip-pink-bg/-fg`, `--chip-amb-bg/-fg`, `--chip-ind-bg/-fg`, `--chip-red-bg`, `--chip-gray-bg/-fg/-gray2-fg`, `--hl-arrow`.

- [ ] **Step 1: Add the mockup's bench-specific tokens to `theme/variables.ts`**

Open `frontend/src/theme/variables.ts`. In the `light` object, after the existing `'--tok-plain': '#1f1a26',` line, add:

```ts
    '--pill': '#ffffff',
    '--chip-vio-bg': '#ede9fe',
    '--chip-vio-fg': '#6d28d9',
    '--chip-teal-bg': '#e0f2f1',
    '--chip-teal-fg': '#0f766e',
    '--chip-pink-bg': '#fce7f3',
    '--chip-pink-fg': '#be185d',
    '--chip-amb-bg': '#fef3c7',
    '--chip-amb-fg': '#b45309',
    '--chip-ind-bg': '#e0e7ff',
    '--chip-ind-fg': '#4338ca',
    '--chip-red-bg': '#fee2e2',
    '--chip-gray-bg': '#f3f4f6',
    '--chip-gray-fg': '#6b7280',
    '--chip-gray2-fg': '#4b5563',
    '--hl-arrow': '#d6336c',
```

In the `dark` object, after the existing `'--tok-plain': '#e7e4ec',` line, add:

```ts
    '--pill': '#37313f',
    '--chip-vio-bg': 'rgba(139,92,246,.18)',
    '--chip-vio-fg': '#c4b5fd',
    '--chip-teal-bg': 'rgba(20,184,166,.15)',
    '--chip-teal-fg': '#5eead4',
    '--chip-pink-bg': 'rgba(236,72,153,.15)',
    '--chip-pink-fg': '#f9a8d4',
    '--chip-amb-bg': 'rgba(245,158,11,.15)',
    '--chip-amb-fg': '#fcd34d',
    '--chip-ind-bg': 'rgba(99,102,241,.18)',
    '--chip-ind-fg': '#a5b4fc',
    '--chip-red-bg': 'rgba(248,113,113,.15)',
    '--chip-gray-bg': 'rgba(231,228,236,.08)',
    '--chip-gray-fg': '#9ca3af',
    '--chip-gray2-fg': '#9ca3af',
    '--hl-arrow': '#f783ac',
```

- [ ] **Step 2: Create the shared style helpers**

Create `frontend/src/benches/components/styles.ts`:

```ts
import type { CSSProperties } from 'react';

/** Font stack for every code/data element across the three bench screens. */
export const monoFont = "'IBM Plex Mono', monospace";

export const sectionLabelStyle: CSSProperties = {
  fontFamily: monoFont,
  fontSize: 9.5,
  letterSpacing: '.14em',
  color: 'var(--text3)',
  textTransform: 'uppercase',
};

export const cardStyle: CSSProperties = {
  background: 'var(--panel)',
  border: '1px solid var(--border)',
  borderRadius: 12,
  padding: '14px 16px',
  display: 'flex',
  flexDirection: 'column',
  gap: 10,
};

export const monoTextareaStyle: CSSProperties = {
  width: '100%',
  boxSizing: 'border-box',
  border: '1px solid var(--border2)',
  borderRadius: 8,
  padding: '11px 13px',
  fontFamily: monoFont,
  fontSize: 12.5,
  lineHeight: 1.55,
  color: 'var(--text)',
  background: 'var(--code)',
  resize: 'vertical',
};

export const monoInputStyle: CSSProperties = {
  border: '1px solid var(--border2)',
  borderRadius: 8,
  padding: '9px 12px',
  fontFamily: monoFont,
  fontSize: 12.5,
  color: 'var(--text)',
  background: 'var(--panel)',
};

export const errorBannerStyle: CSSProperties = {
  padding: '10px 14px',
  borderRadius: 8,
  background: 'var(--fail-bg)',
  border: '1px solid var(--fail-border)',
  fontFamily: monoFont,
  fontSize: 12,
  color: 'var(--fail)',
  lineHeight: 1.5,
};

export const pillGroupStyle: CSSProperties = {
  display: 'flex',
  gap: 2,
  background: 'var(--inset)',
  borderRadius: 8,
  padding: 3,
};

export function pillItemStyle(active: boolean): CSSProperties {
  return {
    padding: '6px 14px',
    borderRadius: 6,
    fontSize: 12.5,
    cursor: 'pointer',
    background: active ? 'var(--pill)' : 'transparent',
    color: active ? 'var(--text)' : 'var(--text3)',
    fontWeight: active ? 600 : 500,
    boxShadow: active ? '0 1px 3px var(--border2)' : 'none',
  };
}

export function chipStyle(bg: string, fg: string): CSSProperties {
  return {
    fontFamily: monoFont,
    fontSize: 9.5,
    fontWeight: 600,
    padding: '3px 8px',
    borderRadius: 99,
    background: bg,
    color: fg,
  };
}

export const primaryButtonStyle: CSSProperties = {
  padding: '8px 18px',
  borderRadius: 8,
  background: 'var(--accent)',
  color: 'var(--accent-contrast)',
  fontSize: 13,
  fontWeight: 600,
  cursor: 'pointer',
  border: 'none',
  boxShadow: 'var(--accent-shadow)',
};

export const engineBadgeStyle: CSSProperties = {
  fontFamily: monoFont,
  fontSize: 10,
  padding: '3px 9px',
  borderRadius: 99,
  background: 'var(--inset)',
  color: 'var(--text3)',
};
```

- [ ] **Step 3: Create the shared primitive components**

Create `frontend/src/benches/components/primitives.tsx`:

```tsx
import type { CSSProperties, ReactNode } from 'react';
import { cardStyle, errorBannerStyle, pillGroupStyle, pillItemStyle } from './styles';

/** A bordered, padded panel used for every card-shaped section across the bench screens. */
export function Card({ children, style }: { children: ReactNode; style?: CSSProperties }) {
  return <div style={style ? { ...cardStyle, ...style } : cardStyle}>{children}</div>;
}

/** Mono-font red banner for evaluation/parse errors. */
export function ErrorBanner({ message }: { message: string }) {
  return <div style={errorBannerStyle}>{message}</div>;
}

export interface PillItem {
  id: string;
  label: string;
}

export interface PillsProps {
  items: PillItem[];
  activeId: string;
  onChange: (id: string) => void;
}

/** Segmented pill tab group, used for both the bench switcher and each bench's result tabs. */
export function Pills({ items, activeId, onChange }: PillsProps) {
  return (
    <div style={pillGroupStyle}>
      {items.map((item) => (
        <span key={item.id} onClick={() => onChange(item.id)} style={pillItemStyle(item.id === activeId)}>
          {item.label}
        </span>
      ))}
    </div>
  );
}
```

- [ ] **Step 4: Create placeholder bench screens**

Create `frontend/src/benches/fhirpath/FhirPathBench.tsx`:

```tsx
export function FhirPathBench() {
  return <div style={{ padding: 24, color: 'var(--text3)' }}>FHIRPath bench — coming in Task 3.</div>;
}
```

Create `frontend/src/benches/fml/FmlBench.tsx`:

```tsx
export function FmlBench() {
  return <div style={{ padding: 24, color: 'var(--text3)' }}>FML bench — coming in Task 5.</div>;
}
```

Create `frontend/src/benches/sof/SofBench.tsx`:

```tsx
export function SofBench() {
  return <div style={{ padding: 24, color: 'var(--text3)' }}>SQL on FHIR bench — coming in Task 6.</div>;
}
```

- [ ] **Step 5: Create the BenchesApp shell**

Create `frontend/src/benches/BenchesApp.tsx`:

```tsx
import { useState, type CSSProperties } from 'react';
import { useTheme } from '../hooks/useTheme';
import { Pills } from './components/primitives';
import { monoFont } from './components/styles';
import { FhirPathBench } from './fhirpath/FhirPathBench';
import { FmlBench } from './fml/FmlBench';
import { SofBench } from './sof/SofBench';

type BenchId = 'fhirpath' | 'fml' | 'sqlonfhir';

const BENCH_TABS: { id: BenchId; label: string }[] = [
  { id: 'fhirpath', label: 'FHIRPath' },
  { id: 'fml', label: 'FML' },
  { id: 'sqlonfhir', label: 'SQL on FHIR' },
];

const shellStyle: CSSProperties = {
  minHeight: '100vh',
  background: 'var(--bg)',
  color: 'var(--text)',
  fontFamily: "'IBM Plex Sans', system-ui, sans-serif",
};

const topBarStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 14,
  padding: '12px 20px',
  background: 'var(--panel)',
  borderBottom: '1px solid var(--border)',
  position: 'sticky',
  top: 0,
  zIndex: 20,
};

/** Top-level shell for the Expression Benches page: top bar + bench-switching tabs. No router — tab state is component-local, same convention as the conformance app's `App.tsx`. */
export function BenchesApp() {
  const theme = useTheme();
  const [bench, setBench] = useState<BenchId>('fhirpath');

  return (
    <div style={{ ...shellStyle, ...(theme.variables as CSSProperties) }}>
      <header style={topBarStyle}>
        <div style={{ display: 'flex', flexDirection: 'column' }}>
          <span style={{ fontSize: 14.5, fontWeight: 700, letterSpacing: '-.01em' }}>Ignixa Lab</span>
          <span style={{ fontFamily: monoFont, fontSize: 9, letterSpacing: '.14em', color: 'var(--text3)', textTransform: 'uppercase' }}>
            Expression benches
          </span>
        </div>

        <div style={{ marginLeft: 20 }}>
          <Pills items={BENCH_TABS} activeId={bench} onChange={(id) => setBench(id as BenchId)} />
        </div>

        <a href="./" style={{ fontSize: 12, fontWeight: 600, color: 'var(--text3)', textDecoration: 'none', padding: '6px 10px' }}>
          Conformance ↗
        </a>

        <div style={{ flex: 1 }} />

        <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text3)' }}>
          {bench === 'fhirpath' ? 'live engine' : 'mock engine · exploration'}
        </span>

        <button
          type="button"
          onClick={theme.toggle}
          title="Toggle theme"
          style={{
            width: 32,
            height: 32,
            display: 'grid',
            placeItems: 'center',
            border: '1px solid var(--border2)',
            borderRadius: 8,
            background: 'transparent',
            color: 'var(--text3)',
            fontSize: 14,
            cursor: 'pointer',
          }}
        >
          {theme.icon}
        </button>
      </header>

      <main>
        {bench === 'fhirpath' ? <FhirPathBench /> : null}
        {bench === 'fml' ? <FmlBench /> : null}
        {bench === 'sqlonfhir' ? <SofBench /> : null}
      </main>
    </div>
  );
}
```

- [ ] **Step 6: Add the second Vite HTML entry**

Create `frontend/benches.html`:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Ignixa Lab · Expression Benches</title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link
      href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600&display=swap"
      rel="stylesheet"
    />
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/benches-main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 7: Add the second entry's React mount point**

Create `frontend/src/benches-main.tsx`:

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import { BenchesApp } from './benches/BenchesApp';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BenchesApp />
  </StrictMode>,
);
```

- [ ] **Step 8: Register the second entry in the Vite build**

Open `frontend/vite.config.ts`. Replace its full contents with:

```ts
import { resolve } from 'node:path'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Served from https://brendankowitz.github.io/ignixa-lab/ (a GitHub Pages
  // project page, not a custom domain), so every built asset URL needs this
  // prefix. Harmless for local dev, which always runs from `/`.
  base: '/ignixa-lab/',
  server: {
    // Proxy API calls to the local Azure Functions host during development so
    // the SPA can use same-origin relative `/api/*` paths without CORS setup.
    proxy: {
      '/api': {
        target: 'http://localhost:7071',
        changeOrigin: true,
      },
    },
  },
  build: {
    rollupOptions: {
      // Multi-page app: the conformance runner (index.html) and Expression
      // Benches (benches.html) are two separate React roots sharing the same
      // theme tokens, not one router-based SPA — see
      // docs/superpowers/specs/2026-07-02-expression-benches-design.md.
      input: {
        main: resolve(__dirname, 'index.html'),
        benches: resolve(__dirname, 'benches.html'),
      },
    },
  },
})
```

- [ ] **Step 9: Add the reciprocal link from the conformance tool's top bar**

Open `frontend/src/components/TopBar.tsx`. In the `TopBar` function's returned JSX, add a link immediately after the closing `</nav>` tag (before the `<div className="top-bar__spacer" />` line):

```tsx
      <a href="benches.html" className="top-bar__nav-item" style={{ textDecoration: 'none' }}>
        Expression Benches ↗
      </a>

```

- [ ] **Step 10: Type-check, lint, and build**

Run:

```bash
cd frontend
npm run build
npm run lint
```

Expected: both commands exit 0. `npm run build` output should list two entry chunks (`index.html` and `benches.html`) in `dist/`.

- [ ] **Step 11: Manual verification**

Run `npm run dev`, open `http://localhost:5173/` and `http://localhost:5173/benches.html`:
- The conformance app's top bar shows a new "Expression Benches ↗" link that navigates to `/benches.html`.
- `/benches.html` shows the Ignixa Lab / Expression Benches top bar, three bench tabs (FHIRPath / FML / SQL on FHIR) that switch the placeholder body text, a "Conformance ↗" link back to `/`, and a working theme toggle (light/dark) that persists across a reload.

- [ ] **Step 12: Commit**

```bash
git add frontend/src/theme/variables.ts frontend/src/benches frontend/benches.html frontend/src/benches-main.tsx frontend/vite.config.ts frontend/src/components/TopBar.tsx
git commit -m "Add Expression Benches shell: multi-page scaffold, shared primitives, theme tokens"
```

---

## Task 2: FHIRPath bench — data layer

**Files:**
- Create: `frontend/src/benches/fhirpath/fhirPathTypes.ts`
- Create: `frontend/src/benches/fhirpath/sampleResources.ts`
- Create: `frontend/src/benches/fhirpath/fhirPathApi.ts`
- Create: `frontend/src/benches/fhirpath/useFhirPathEval.ts`

**Interfaces:**
- Consumes: none (new subtree).
- Produces: types `FhirVersion`, `FpVariable`, `FpResultItem`, `FpResultGroup`, `FpTraceRow`, `FpAstNode`, `FpEvalResult` from `fhirPathTypes.ts`; `SAMPLE_RESOURCES: FhirResourceFixture[]`, `EXAMPLE_EXPRESSIONS`, `DEFAULT_EXPRESSION` from `sampleResources.ts`; `buildFhirPathRequest(input)`, `runFhirPath(version, body, signal)`, `parseFhirPathResponse(response)` from `fhirPathApi.ts`; `useFhirPathEval(input): {result: FpEvalResult; isLoading: boolean}` from `useFhirPathEval.ts`. Consumed by Task 3's `FhirPathBench.tsx`.

- [ ] **Step 1: Define the FHIRPath bench's types**

Create `frontend/src/benches/fhirpath/fhirPathTypes.ts`:

```ts
export type FhirVersion = 'stu3' | 'r4' | 'r4b' | 'r5' | 'r6';

export interface FpVariable {
  name: string;
  value: string;
}

export interface FpResultItem {
  type: string;
  text: string;
}

export interface FpResultGroup {
  label: string | null;
  items: FpResultItem[];
}

export interface FpTraceRow {
  label: string;
  items: FpResultItem[];
}

export interface FpAstNode {
  expressionType: string;
  name: string;
  returnType: string | null;
  arguments: FpAstNode[];
}

export interface FpEvalResult {
  error: string | null;
  evaluator: string;
  groups: FpResultGroup[];
  trace: FpTraceRow[];
  ast: FpAstNode | null;
}

/** A generic FHIR `Parameters.parameter[]` entry — permissive enough to cover every `value[x]`/`resource`/`part` shape the backend emits. */
export interface FhirParameter {
  name: string;
  part?: FhirParameter[];
  resource?: unknown;
  extension?: { url: string; valueString?: string }[];
  [valueKey: string]: unknown;
}

export interface FhirParameters {
  resourceType: 'Parameters';
  id?: string;
  parameter?: FhirParameter[];
}
```

- [ ] **Step 2: Port the mockup's sample resources and example expressions**

Create `frontend/src/benches/fhirpath/sampleResources.ts`:

```ts
export type SampleId = 'patient' | 'observation';

export interface FhirResourceFixture {
  id: SampleId;
  label: string;
  data: Record<string, unknown>;
}

export const PATIENT_EXAMPLE: Record<string, unknown> = {
  resourceType: 'Patient',
  id: 'example',
  active: true,
  name: [
    { use: 'official', family: 'Chalmers', given: ['Peter', 'James'] },
    { use: 'usual', given: ['Jim'] },
    { use: 'maiden', family: 'Windsor', given: ['Peter', 'James'], period: { end: '2002' } },
  ],
  telecom: [
    { system: 'phone', value: '(03) 5555 6473', use: 'work', rank: 1 },
    { system: 'email', value: 'p.chalmers@example.org', use: 'home' },
  ],
  gender: 'male',
  birthDate: '1974-12-25',
  address: [{ use: 'home', line: ['534 Erewhon St'], city: 'PleasantVille', state: 'Vic', postalCode: '3999' }],
};

export const OBSERVATION_EXAMPLE: Record<string, unknown> = {
  resourceType: 'Observation',
  id: 'blood-pressure',
  status: 'final',
  category: [
    {
      coding: [
        { system: 'http://terminology.hl7.org/CodeSystem/observation-category', code: 'vital-signs', display: 'Vital Signs' },
      ],
    },
  ],
  code: {
    coding: [{ system: 'http://loinc.org', code: '85354-9', display: 'Blood pressure panel' }],
    text: 'Blood pressure',
  },
  subject: { reference: 'Patient/example' },
  effectiveDateTime: '2026-05-02T09:30:00Z',
  component: [
    {
      code: { coding: [{ system: 'http://loinc.org', code: '8480-6', display: 'Systolic blood pressure' }] },
      valueQuantity: { value: 127, unit: 'mmHg', system: 'http://unitsofmeasure.org', code: 'mm[Hg]' },
    },
    {
      code: { coding: [{ system: 'http://loinc.org', code: '8462-4', display: 'Diastolic blood pressure' }] },
      valueQuantity: { value: 81, unit: 'mmHg', system: 'http://unitsofmeasure.org', code: 'mm[Hg]' },
    },
  ],
};

export const SAMPLE_RESOURCES: FhirResourceFixture[] = [
  { id: 'patient', label: 'Patient', data: PATIENT_EXAMPLE },
  { id: 'observation', label: 'Observation', data: OBSERVATION_EXAMPLE },
];

export const EXAMPLE_EXPRESSIONS: Record<SampleId, string[]> = {
  patient: [
    "name.where(use = 'official').given.first()",
    "telecom.where(system = 'phone').value",
    'name.given.count()',
    "name.select(given.first() & ' ' & family)",
  ],
  observation: [
    "component.where(code.coding.code = '8480-6').value.value",
    "code.coding.display.join(' / ')",
    'component.count()',
    'effective',
  ],
};

export const DEFAULT_EXPRESSION = "name.where(use = 'official').given.first()";
```

- [ ] **Step 3: Build the request builder and response parser**

Create `frontend/src/benches/fhirpath/fhirPathApi.ts`:

```ts
import type {
  FhirParameter,
  FhirParameters,
  FhirVersion,
  FpAstNode,
  FpEvalResult,
  FpResultItem,
  FpVariable,
} from './fhirPathTypes';

const ROUTE_BY_VERSION: Record<FhirVersion, string> = {
  stu3: '$fhirpath-stu3',
  r4: '$fhirpath-r4',
  r4b: '$fhirpath-r4b',
  r5: '$fhirpath-r5',
  r6: '$fhirpath-r6',
};

const JSON_VALUE_EXTENSION_URL = 'http://fhir.forms-lab.com/StructureDefinition/json-value';

export interface FhirPathRequestInput {
  expression: string;
  context: string;
  resourceText: string;
  variables: FpVariable[];
}

/** Builds the FHIR `Parameters` request body per `server-api.md`. Throws if `resourceText` isn't valid JSON — callers should catch and surface it as a resource-JSON error. */
export function buildFhirPathRequest(input: FhirPathRequestInput): FhirParameters {
  const parameter: FhirParameter[] = [{ name: 'expression', valueString: input.expression }];

  if (input.context.trim()) {
    parameter.push({ name: 'context', valueString: input.context });
  }

  const namedVariables = input.variables.filter((variable) => variable.name.trim());
  if (namedVariables.length > 0) {
    parameter.push({
      name: 'variables',
      part: namedVariables.map((variable) => ({ name: variable.name, valueString: variable.value })),
    });
  }

  parameter.push({ name: 'resource', resource: JSON.parse(input.resourceText) });

  return { resourceType: 'Parameters', parameter };
}

/** Extracts a readable message from an `OperationOutcome` error response, if that's what the body is. */
function readOperationOutcomeMessage(body: unknown): string | null {
  const outcome = body as { resourceType?: string; issue?: { details?: { text?: string }; diagnostics?: string }[] };
  if (outcome?.resourceType !== 'OperationOutcome' || !outcome.issue?.length) {
    return null;
  }
  const issue = outcome.issue[0];
  return issue.details?.text ?? issue.diagnostics ?? null;
}

/** POSTs to the FHIRPath evaluator route for the given version and returns the raw `Parameters` response. Throws on a non-2xx response or a network/abort error. */
export async function runFhirPath(
  version: FhirVersion,
  body: FhirParameters,
  signal: AbortSignal,
): Promise<FhirParameters> {
  const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
  const response = await fetch(`${apiBaseUrl}/api/${ROUTE_BY_VERSION[version]}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  });

  const json = (await response.json()) as FhirParameters;
  if (!response.ok) {
    throw new Error(readOperationOutcomeMessage(json) ?? `Request failed with status ${response.status}`);
  }
  return json;
}

/** Reads a result/trace part's value from `value{Type}`, `resource`, or the json-value extension fallback. */
function readPartValue(part: FhirParameter): string {
  if (part.resource !== undefined) {
    return JSON.stringify(part.resource, null, 2);
  }

  const jsonExtension = part.extension?.find((extension) => extension.url === JSON_VALUE_EXTENSION_URL);
  if (jsonExtension?.valueString !== undefined) {
    try {
      return JSON.stringify(JSON.parse(jsonExtension.valueString), null, 2);
    } catch {
      return jsonExtension.valueString;
    }
  }

  const valueKey = Object.keys(part).find((key) => key.startsWith('value'));
  if (valueKey) {
    const value = part[valueKey];
    return typeof value === 'object' && value !== null ? JSON.stringify(value, null, 2) : JSON.stringify(value);
  }

  return '';
}

function toResultItem(part: FhirParameter): FpResultItem {
  return { type: part.name, text: readPartValue(part) };
}

interface RawAstNode {
  ExpressionType: string;
  Name: string;
  ReturnType?: string;
  Arguments?: RawAstNode[];
}

function parseAstNode(raw: RawAstNode): FpAstNode {
  return {
    expressionType: raw.ExpressionType,
    name: raw.Name,
    returnType: raw.ReturnType ?? null,
    arguments: Array.isArray(raw.Arguments) ? raw.Arguments.map(parseAstNode) : [],
  };
}

/** Parses the FHIRPath evaluator's `Parameters` response into the shape the bench UI renders. */
export function parseFhirPathResponse(response: FhirParameters): FpEvalResult {
  const parameters = response.parameter ?? [];
  const emptyResult: FpEvalResult = { error: null, evaluator: '', groups: [], trace: [], ast: null };

  const errorParameter = parameters.find((parameter) => parameter.name === 'error');
  if (errorParameter) {
    return { ...emptyResult, error: (errorParameter.valueString as string) ?? 'Unknown error' };
  }

  const configPart = parameters.find((parameter) => parameter.name === 'parameters');
  const evaluatorPart = configPart?.part?.find((part) => part.name === 'evaluator');
  const evaluator = (evaluatorPart?.valueString as string) ?? '';

  const astPart = configPart?.part?.find((part) => part.name === 'parseDebugTree');
  let ast: FpAstNode | null = null;
  if (typeof astPart?.valueString === 'string') {
    try {
      ast = parseAstNode(JSON.parse(astPart.valueString) as RawAstNode);
    } catch {
      ast = null;
    }
  }

  const groups = [];
  const trace = [];
  for (const resultParameter of parameters.filter((parameter) => parameter.name === 'result')) {
    const items: FpResultItem[] = [];
    for (const part of resultParameter.part ?? []) {
      if (part.name === 'trace') {
        trace.push({ label: (part.valueString as string) ?? '', items: (part.part ?? []).map(toResultItem) });
      } else {
        items.push(toResultItem(part));
      }
    }
    groups.push({ label: (resultParameter.valueString as string) ?? null, items });
  }

  return { error: null, evaluator, groups, trace, ast };
}
```

- [ ] **Step 4: Build the debounced evaluation hook**

Create `frontend/src/benches/fhirpath/useFhirPathEval.ts`:

```ts
import { useEffect, useRef, useState } from 'react';
import { buildFhirPathRequest, parseFhirPathResponse, runFhirPath } from './fhirPathApi';
import type { FhirVersion, FpEvalResult, FpVariable } from './fhirPathTypes';

export interface FhirPathEvalInput {
  version: FhirVersion;
  expression: string;
  context: string;
  resourceText: string;
  variables: FpVariable[];
}

const DEBOUNCE_MS = 450;

const EMPTY_RESULT: FpEvalResult = { error: null, evaluator: '', groups: [], trace: [], ast: null };

/** Debounced, abortable evaluator: re-POSTs to the FHIRPath backend ~450ms after the last change to any input field, cancelling any still-in-flight request first. */
export function useFhirPathEval(input: FhirPathEvalInput): { result: FpEvalResult; isLoading: boolean } {
  const [result, setResult] = useState<FpEvalResult>(EMPTY_RESULT);
  const [isLoading, setIsLoading] = useState(false);
  const abortControllerRef = useRef<AbortController | null>(null);

  const variablesKey = JSON.stringify(input.variables);

  useEffect(() => {
    if (!input.expression.trim()) {
      abortControllerRef.current?.abort();
      setResult(EMPTY_RESULT);
      setIsLoading(false);
      return;
    }

    const timer = setTimeout(() => {
      abortControllerRef.current?.abort();
      const controller = new AbortController();
      abortControllerRef.current = controller;
      setIsLoading(true);

      let body;
      try {
        body = buildFhirPathRequest(input);
      } catch (error) {
        setIsLoading(false);
        setResult({ ...EMPTY_RESULT, error: `Resource JSON — ${(error as Error).message}` });
        return;
      }

      runFhirPath(input.version, body, controller.signal)
        .then((response) => setResult(parseFhirPathResponse(response)))
        .catch((error: Error) => {
          if (error.name === 'AbortError') {
            return;
          }
          setResult({ ...EMPTY_RESULT, error: error.message });
        })
        .finally(() => {
          if (abortControllerRef.current === controller) {
            setIsLoading(false);
          }
        });
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
    // input.variables is a fresh array each render; variablesKey is the stable dependency for it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [input.version, input.expression, input.context, input.resourceText, variablesKey]);

  return { result, isLoading };
}
```

- [ ] **Step 5: Type-check**

Run:

```bash
cd frontend
npm run build
```

Expected: exits 0. (No visible UI change yet — this data layer is exercised by Task 3's component.)

- [ ] **Step 6: Commit**

```bash
git add frontend/src/benches/fhirpath/fhirPathTypes.ts frontend/src/benches/fhirpath/sampleResources.ts frontend/src/benches/fhirpath/fhirPathApi.ts frontend/src/benches/fhirpath/useFhirPathEval.ts
git commit -m "Add FHIRPath bench data layer: request/response mapping, debounced eval hook"
```

---

## Task 3: FHIRPath bench — UI

**Files:**
- Modify: `frontend/src/benches/fhirpath/FhirPathBench.tsx` (replaces Task 1's placeholder)

**Interfaces:**
- Consumes: everything from Task 2 (`fhirPathTypes.ts`, `sampleResources.ts`, `useFhirPathEval.ts`), and `Card`/`ErrorBanner`/`Pills`/`PillItem` + style helpers from Task 1.
- Produces: `FhirPathBench()` component, already wired into `BenchesApp.tsx` from Task 1 — no further wiring needed.

- [ ] **Step 1: Write the full FHIRPath bench component**

Replace the contents of `frontend/src/benches/fhirpath/FhirPathBench.tsx`:

```tsx
import { useState, type CSSProperties } from 'react';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoInputStyle, monoTextareaStyle, sectionLabelStyle, chipStyle } from '../components/styles';
import { DEFAULT_EXPRESSION, EXAMPLE_EXPRESSIONS, SAMPLE_RESOURCES, type SampleId } from './sampleResources';
import { useFhirPathEval } from './useFhirPathEval';
import type { FhirVersion, FpAstNode, FpVariable } from './fhirPathTypes';

const VERSION_ITEMS: PillItem[] = [
  { id: 'stu3', label: 'STU3' },
  { id: 'r4', label: 'R4' },
  { id: 'r4b', label: 'R4B' },
  { id: 'r5', label: 'R5' },
  { id: 'r6', label: 'R6' },
];

const RESULT_TAB_ITEMS: PillItem[] = [
  { id: 'results', label: 'Results' },
  { id: 'trace', label: 'Trace' },
  { id: 'ast', label: 'Parse tree' },
];

type ResultTab = 'results' | 'trace' | 'ast';

function typeChipColors(type: string): { bg: string; fg: string } {
  if (['string', 'date', 'dateTime'].includes(type)) {
    return { bg: 'var(--pass-bg)', fg: 'var(--pass)' };
  }
  if (['integer', 'decimal', 'boolean'].includes(type)) {
    return { bg: 'var(--chip-teal-bg)', fg: 'var(--chip-teal-fg)' };
  }
  if (['null', 'collection', 'object'].includes(type)) {
    return { bg: 'var(--chip-gray-bg)', fg: 'var(--chip-gray-fg)' };
  }
  return { bg: 'var(--chip-vio-bg)', fg: 'var(--chip-vio-fg)' };
}

function astChipColors(expressionType: string): { bg: string; fg: string } {
  switch (expressionType) {
    case 'FunctionCallExpression':
      return { bg: 'var(--chip-pink-bg)', fg: 'var(--chip-pink-fg)' };
    case 'ChildExpression':
    case 'PropertyAccessExpression':
      return { bg: 'var(--chip-teal-bg)', fg: 'var(--chip-teal-fg)' };
    case 'ConstantExpression':
      return { bg: 'var(--chip-amb-bg)', fg: 'var(--chip-amb-fg)' };
    case 'BinaryExpression':
    case 'UnaryExpression':
      return { bg: 'var(--chip-ind-bg)', fg: 'var(--chip-ind-fg)' };
    case 'VariableRefExpression':
      return { bg: 'var(--chip-red-bg)', fg: 'var(--fail)' };
    default:
      return { bg: 'var(--chip-gray-bg)', fg: 'var(--chip-gray2-fg)' };
  }
}

function AstRows({ node, depth }: { node: FpAstNode; depth: number }) {
  const colors = astChipColors(node.expressionType);
  return (
    <>
      <div style={{ padding: `3px 0 3px ${depth * 18 + 2}px`, display: 'flex', gap: 8, alignItems: 'baseline' }}>
        <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)' }}>├─</span>
        <span style={chipStyle(colors.bg, colors.fg)}>{node.expressionType}</span>
        <span style={{ fontFamily: monoFont, fontSize: 12, color: 'var(--text)' }}>
          {node.name}
          {node.returnType ? ` : ${node.returnType}` : ''}
        </span>
      </div>
      {node.arguments.map((child, index) => (
        <AstRows key={index} node={child} depth={depth + 1} />
      ))}
    </>
  );
}

const twoColumnStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'minmax(340px,42%) 1fr',
  gap: 14,
  alignItems: 'start',
};

export function FhirPathBench() {
  const [version, setVersion] = useState<FhirVersion>('r4');
  const [expression, setExpression] = useState(DEFAULT_EXPRESSION);
  const [context, setContext] = useState('');
  const [sampleId, setSampleId] = useState<SampleId>('patient');
  const [resourceText, setResourceText] = useState(() => JSON.stringify(SAMPLE_RESOURCES[0].data, null, 2));
  const [variables, setVariables] = useState<FpVariable[]>([]);
  const [resultTab, setResultTab] = useState<ResultTab>('results');

  const { result, isLoading } = useFhirPathEval({ version, expression, context, resourceText, variables });

  const updateVariable = (index: number, patch: Partial<FpVariable>) =>
    setVariables((current) => current.map((variable, i) => (i === index ? { ...variable, ...patch } : variable)));

  const removeVariable = (index: number) => setVariables((current) => current.filter((_, i) => i !== index));

  return (
    <div style={{ maxWidth: 1280, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>FHIRPath</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>
          Evaluate expressions against a resource — results, trace, and parse tree.
        </span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>{result.evaluator || (isLoading ? 'evaluating…' : 'ignixa-lab')}</span>
      </div>

      <Card>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span style={sectionLabelStyle}>Expression</span>
          <textarea
            value={expression}
            onChange={(event) => setExpression(event.target.value)}
            spellCheck={false}
            rows={2}
            style={monoTextareaStyle}
          />
        </div>

        <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: 1, minWidth: 260 }}>
            <span style={sectionLabelStyle}>
              Context expression <span style={{ textTransform: 'none', letterSpacing: 0, color: 'var(--text4)' }}>· optional, evaluates per item</span>
            </span>
            <input
              value={context}
              onChange={(event) => setContext(event.target.value)}
              spellCheck={false}
              placeholder="e.g. name"
              style={monoInputStyle}
            />
          </div>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center', paddingBottom: 2 }}>
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>Examples</span>
            {EXAMPLE_EXPRESSIONS[sampleId].map((example) => (
              <span
                key={example}
                onClick={() => setExpression(example)}
                style={{
                  fontFamily: monoFont,
                  fontSize: 10.5,
                  padding: '5px 10px',
                  borderRadius: 99,
                  border: '1px solid var(--border)',
                  color: 'var(--text2)',
                  cursor: 'pointer',
                  background: 'var(--panel)',
                }}
              >
                {example.length > 44 ? `${example.slice(0, 44)}…` : example}
              </span>
            ))}
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={sectionLabelStyle}>Variables</span>
            <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)' }}>%resource · %context built-in</span>
            <span
              onClick={() => setVariables((current) => [...current, { name: '', value: '' }])}
              style={{ fontSize: 11.5, fontWeight: 600, color: 'var(--accent)', cursor: 'pointer' }}
            >
              + Add variable
            </span>
          </div>
          {variables.map((variable, index) => (
            <div key={index} style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <span style={{ fontFamily: monoFont, fontSize: 12, color: 'var(--text3)' }}>%</span>
              <input
                value={variable.name}
                onChange={(event) => updateVariable(index, { name: event.target.value })}
                placeholder="name"
                spellCheck={false}
                style={{ ...monoInputStyle, width: 140 }}
              />
              <input
                value={variable.value}
                onChange={(event) => updateVariable(index, { value: event.target.value })}
                placeholder="value (JSON or string)"
                spellCheck={false}
                style={{ ...monoInputStyle, flex: 1 }}
              />
              <span
                onClick={() => removeVariable(index)}
                style={{ width: 24, height: 24, display: 'grid', placeItems: 'center', borderRadius: 6, color: 'var(--text4)', cursor: 'pointer', fontSize: 12 }}
              >
                ✕
              </span>
            </div>
          ))}
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span style={sectionLabelStyle}>FHIR version</span>
          <Pills items={VERSION_ITEMS} activeId={version} onChange={(id) => setVersion(id as FhirVersion)} />
        </div>
      </Card>

      <div style={twoColumnStyle}>
        <Card>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Test resource</span>
            {SAMPLE_RESOURCES.map((sample) => (
              <span
                key={sample.id}
                onClick={() => {
                  setSampleId(sample.id);
                  setResourceText(JSON.stringify(sample.data, null, 2));
                }}
                style={{
                  fontSize: 11,
                  fontWeight: 600,
                  padding: '4px 11px',
                  borderRadius: 99,
                  cursor: 'pointer',
                  background: sampleId === sample.id ? 'var(--chip-vio-bg)' : 'var(--panel)',
                  color: sampleId === sample.id ? 'var(--chip-vio-fg)' : 'var(--text3)',
                  border: `1px solid ${sampleId === sample.id ? 'var(--accent-border)' : 'var(--border2)'}`,
                }}
              >
                {sample.label}
              </span>
            ))}
          </div>
          <textarea
            value={resourceText}
            onChange={(event) => setResourceText(event.target.value)}
            spellCheck={false}
            style={{ ...monoTextareaStyle, minHeight: 520, fontSize: 11.5 }}
          />
        </Card>

        <Card style={{ minHeight: 400 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <Pills items={RESULT_TAB_ITEMS} activeId={resultTab} onChange={(id) => setResultTab(id as ResultTab)} />
            <div style={{ flex: 1 }} />
            {isLoading ? <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>evaluating…</span> : null}
          </div>

          {result.error ? <ErrorBanner message={result.error} /> : null}

          {!result.error && resultTab === 'results'
            ? result.groups.map((group, groupIndex) => (
                <div key={groupIndex} style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  {group.label ? (
                    <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--accent)', paddingTop: 4 }}>{group.label}</span>
                  ) : null}
                  {group.items.length === 0 ? (
                    <div
                      style={{
                        padding: '8px 12px',
                        borderRadius: 8,
                        border: '1px dashed var(--border2)',
                        fontFamily: monoFont,
                        fontSize: 11.5,
                        color: 'var(--text4)',
                      }}
                    >
                      empty collection · {'{ }'}
                    </div>
                  ) : (
                    group.items.map((item, itemIndex) => {
                      const colors = typeChipColors(item.type);
                      return (
                        <div
                          key={itemIndex}
                          style={{
                            display: 'flex',
                            gap: 12,
                            alignItems: 'flex-start',
                            padding: '9px 12px',
                            borderRadius: 8,
                            background: 'var(--code)',
                            border: '1px solid var(--border)',
                          }}
                        >
                          <span style={chipStyle(colors.bg, colors.fg)}>{item.type}</span>
                          <pre style={{ margin: 0, flex: 1, minWidth: 0, fontFamily: monoFont, fontSize: 12, lineHeight: 1.55, whiteSpace: 'pre-wrap', wordBreak: 'break-word', color: 'var(--text)' }}>
                            {item.text}
                          </pre>
                        </div>
                      );
                    })
                  )}
                </div>
              ))
            : null}

          {!result.error && resultTab === 'trace'
            ? result.trace.map((row, rowIndex) => (
                <div key={rowIndex} style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                  <span style={{ fontFamily: monoFont, fontSize: 11, fontWeight: 600, color: 'var(--chip-amb-fg)' }}>trace('{row.label}')</span>
                  {row.items.map((item, itemIndex) => (
                    <pre key={itemIndex} style={{ margin: '0 0 0 16px', fontFamily: monoFont, fontSize: 11.5, color: 'var(--text2)' }}>
                      {item.type}: {item.text}
                    </pre>
                  ))}
                </div>
              ))
            : null}
          {!result.error && resultTab === 'trace' && result.trace.length === 0 ? (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>
              No trace output — add <span style={{ fontFamily: monoFont }}>.trace('label')</span> anywhere in the expression to log a checkpoint.
            </span>
          ) : null}

          {!result.error && resultTab === 'ast' && result.ast ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 1, padding: '4px 2px' }}>
              <AstRows node={result.ast} depth={0} />
            </div>
          ) : null}
        </Card>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Type-check, lint, and build**

Run:

```bash
cd frontend
npm run build
npm run lint
```

Expected: both exit 0.

- [ ] **Step 3: Manual verification against the local backend**

Start the backend (`cd backend/src/Ignixa.Lab.Functions && func start`) and the frontend dev server (`cd frontend && npm run dev`). Open `http://localhost:5173/benches.html`, FHIRPath tab:
- Default expression `name.where(use = 'official').given.first()` against the default Patient resource returns `"Peter"` in the Results tab within ~1s of load (auto-run fires once on mount since a default expression is present).
- Switching the "Observation" sample chip swaps in the Observation fixture and its own example chips.
- Typing `given.trace('given values').join(' ')` and waiting ~500ms shows a debounced re-run and a `trace('given values')` row under the Trace tab.
- The Parse tree tab shows an indented node tree for the current expression.
- Entering an invalid expression (e.g. unbalanced parens) shows the red error banner.
- Switching the FHIR version pill (e.g. to R4B) triggers a new evaluation against `$fhirpath-r4b`.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/benches/fhirpath/FhirPathBench.tsx
git commit -m "Wire FHIRPath bench UI to the real backend"
```

---

## Task 4: Shared mini FHIRPath evaluator (for FML/SoF mocks)

**Files:**
- Create: `frontend/src/benches/shared/miniFhirPath.ts`

**Interfaces:**
- Consumes: none.
- Produces: `parseMiniFhirPath(source: string): MiniNode`, `evaluateMiniFhirPath(node: MiniNode, collection: unknown[], env: MiniEnv): unknown[]`, `MiniEnv = {vars: Record<string, unknown>}`. Consumed by Task 5's `fmlEngine.ts` and Task 6's `sofEngine.ts` — neither the FHIRPath bench (Task 3, real backend) nor anything else uses this module.

- [ ] **Step 1: Port the mockup's client-side FHIRPath-subset tokenizer, parser, and evaluator**

Create `frontend/src/benches/shared/miniFhirPath.ts`:

```ts
/**
 * A small client-side FHIRPath-subset engine, ported from the Expression
 * Benches mockup. Used only to power the FML and SQL-on-FHIR *mock* benches'
 * own path evaluation (`forEach`, `column.path`, FML rule right-hand sides) —
 * the real FHIRPath bench talks to the actual backend engine and does not
 * use this module. Not a full FHIRPath implementation.
 */

type MiniToken =
  | { t: 'id'; v: string }
  | { t: 'num'; v: number }
  | { t: 'str'; v: string }
  | { t: 'var'; v: string }
  | { t: 'op'; v: string };

export type MiniNode =
  | { k: 'lit'; v: string | number | boolean }
  | { k: 'var'; v: string }
  | { k: 'paren'; e: MiniNode }
  | { k: 'path'; name: string }
  | { k: 'fn'; name: string; args: MiniNode[] }
  | { k: 'chain'; parts: MiniNode[] }
  | { k: 'index'; e: MiniNode }
  | { k: 'bin'; op: string; l: MiniNode; r: MiniNode };

export interface MiniEnv {
  vars: Record<string, unknown>;
}

function tokenize(source: string): MiniToken[] {
  const tokens: MiniToken[] = [];
  let i = 0;
  const isIdStart = (c: string) => /[A-Za-z_$]/.test(c);
  const isIdChar = (c: string) => /[A-Za-z0-9_$]/.test(c);

  while (i < source.length) {
    const c = source[i];
    if (/\s/.test(c)) {
      i++;
      continue;
    }
    if (isIdStart(c)) {
      let j = i + 1;
      while (j < source.length && isIdChar(source[j])) j++;
      tokens.push({ t: 'id', v: source.slice(i, j) });
      i = j;
      continue;
    }
    if (/[0-9]/.test(c)) {
      let j = i;
      while (j < source.length && (/[0-9]/.test(source[j]) || (source[j] === '.' && /[0-9]/.test(source[j + 1] ?? '')))) j++;
      tokens.push({ t: 'num', v: Number.parseFloat(source.slice(i, j)) });
      i = j;
      continue;
    }
    if (c === "'") {
      let j = i + 1;
      let value = '';
      while (j < source.length && source[j] !== "'") {
        value += source[j];
        j++;
      }
      if (j >= source.length) throw new Error('Unterminated string literal');
      tokens.push({ t: 'str', v: value });
      i = j + 1;
      continue;
    }
    if (c === '%') {
      let j = i + 1;
      while (j < source.length && isIdChar(source[j])) j++;
      tokens.push({ t: 'var', v: source.slice(i + 1, j) });
      i = j;
      continue;
    }
    const two = source.slice(i, i + 2);
    if (two === '!=' || two === '<=' || two === '>=') {
      tokens.push({ t: 'op', v: two });
      i += 2;
      continue;
    }
    if ('.()[],=<>|+-*/&'.includes(c)) {
      tokens.push({ t: 'op', v: c });
      i++;
      continue;
    }
    throw new Error(`Unexpected character '${c}' at position ${i}`);
  }
  return tokens;
}

/** Parses a FHIRPath-subset expression into a {@link MiniNode} tree. */
export function parseMiniFhirPath(source: string): MiniNode {
  const tokens = tokenize(source);
  let position = 0;
  const peek = () => tokens[position];
  const eat = () => tokens[position++];
  const expect = (value: string) => {
    const token = eat();
    if (!token || token.v !== value) throw new Error(`Expected '${value}'`);
  };

  const parseArgs = (): MiniNode[] => {
    const args: MiniNode[] = [];
    if (peek() && peek().v !== ')') {
      args.push(parseExpr());
      while (peek() && peek().v === ',') {
        eat();
        args.push(parseExpr());
      }
    }
    expect(')');
    return args;
  };

  const parsePrimary = (): MiniNode => {
    const token = peek();
    if (!token) throw new Error('Unexpected end of expression');
    if (token.t === 'num' || token.t === 'str') {
      eat();
      return { k: 'lit', v: token.v };
    }
    if (token.t === 'var') {
      eat();
      return { k: 'var', v: token.v };
    }
    if (token.t === 'op' && token.v === '(') {
      eat();
      const inner = parseExpr();
      expect(')');
      return { k: 'paren', e: inner };
    }
    if (token.t === 'id') {
      eat();
      if (token.v === 'true' || token.v === 'false') return { k: 'lit', v: token.v === 'true' };
      if (peek() && peek().v === '(') {
        eat();
        return { k: 'fn', name: token.v, args: parseArgs() };
      }
      return { k: 'path', name: token.v };
    }
    throw new Error(`Unexpected token '${token.v}'`);
  };

  const parsePostfix = (): MiniNode => {
    const first = parsePrimary();
    const parts = [first];
    while (peek()) {
      if (peek().v === '.') {
        eat();
        const token = eat();
        if (!token || token.t !== 'id') throw new Error("Expected a name after '.'");
        if (peek() && peek().v === '(') {
          eat();
          parts.push({ k: 'fn', name: token.v, args: parseArgs() });
        } else {
          parts.push({ k: 'path', name: token.v });
        }
      } else if (peek().v === '[') {
        eat();
        const inner = parseExpr();
        expect(']');
        parts.push({ k: 'index', e: inner });
      } else {
        break;
      }
    }
    return parts.length === 1 ? first : { k: 'chain', parts };
  };

  const parseMul = (): MiniNode => {
    let left = parsePostfix();
    while (peek() && (peek().v === '*' || peek().v === '/')) {
      const op = eat().v;
      left = { k: 'bin', op, l: left, r: parsePostfix() };
    }
    return left;
  };

  const parseAdd = (): MiniNode => {
    let left = parseMul();
    while (peek() && ['+', '-', '&'].includes(peek().v)) {
      const op = eat().v;
      left = { k: 'bin', op, l: left, r: parseMul() };
    }
    return left;
  };

  const parseUnion = (): MiniNode => {
    let left = parseAdd();
    while (peek() && peek().v === '|') {
      eat();
      left = { k: 'bin', op: '|', l: left, r: parseAdd() };
    }
    return left;
  };

  const parseCmp = (): MiniNode => {
    let left = parseUnion();
    while (peek() && ['<', '>', '<=', '>='].includes(peek().v)) {
      const op = eat().v;
      left = { k: 'bin', op, l: left, r: parseUnion() };
    }
    return left;
  };

  const parseEq = (): MiniNode => {
    let left = parseCmp();
    while (peek() && (peek().v === '=' || peek().v === '!=')) {
      const op = eat().v;
      left = { k: 'bin', op, l: left, r: parseCmp() };
    }
    return left;
  };

  const parseAnd = (): MiniNode => {
    let left = parseEq();
    while (peek() && peek().t === 'id' && peek().v === 'and') {
      eat();
      left = { k: 'bin', op: 'and', l: left, r: parseEq() };
    }
    return left;
  };

  function parseExpr(): MiniNode {
    let left = parseAnd();
    while (peek() && peek().t === 'id' && peek().v === 'or') {
      eat();
      left = { k: 'bin', op: 'or', l: left, r: parseAnd() };
    }
    return left;
  }

  const result = parseExpr();
  if (position < tokens.length) throw new Error(`Unexpected token '${tokens[position].v}'`);
  return result;
}

function getField(item: unknown, name: string, out: unknown[]): void {
  if (item === null || typeof item !== 'object') return;
  const record = item as Record<string, unknown>;
  let value = record[name];
  if (value === undefined) {
    for (const key of Object.keys(record)) {
      if (key.length > name.length && key.startsWith(name) && /[A-Z]/.test(key[name.length])) {
        value = record[key];
        break;
      }
    }
  }
  if (value === undefined) return;
  if (Array.isArray(value)) out.push(...value);
  else out.push(value);
}

function isTruthy(collection: unknown[]): boolean {
  return collection.length > 0 && collection[0] !== false;
}

function evalFunction(node: Extract<MiniNode, { k: 'fn' }>, collection: unknown[], env: MiniEnv): unknown[] {
  const args = node.args;
  const evalArg = (index: number) => evaluateMiniFhirPath(args[index], collection, env);
  switch (node.name) {
    case 'where':
      return collection.filter((item) => isTruthy(evaluateMiniFhirPath(args[0], [item], env)));
    case 'select': {
      const out: unknown[] = [];
      for (const item of collection) out.push(...evaluateMiniFhirPath(args[0], [item], env));
      return out;
    }
    case 'exists':
      return [args.length ? collection.some((item) => isTruthy(evaluateMiniFhirPath(args[0], [item], env))) : collection.length > 0];
    case 'empty':
      return [collection.length === 0];
    case 'count':
      return [collection.length];
    case 'first':
      return collection.slice(0, 1);
    case 'last':
      return collection.slice(-1);
    case 'tail':
      return collection.slice(1);
    case 'join': {
      const separator = args.length ? String(evalArg(0)[0]) : '';
      return [collection.map((x) => (typeof x === 'object' && x !== null ? JSON.stringify(x) : String(x))).join(separator)];
    }
    case 'distinct': {
      const seen = new Set<string>();
      const out: unknown[] = [];
      for (const item of collection) {
        const key = JSON.stringify(item);
        if (!seen.has(key)) {
          seen.add(key);
          out.push(item);
        }
      }
      return out;
    }
    case 'not':
      return collection.length === 0 ? [] : [!isTruthy(collection)];
    case 'toString':
      return collection.length ? [typeof collection[0] === 'object' ? JSON.stringify(collection[0]) : String(collection[0])] : [];
    default:
      throw new Error(`Unsupported function: ${node.name}()`);
  }
}

/** Evaluates a {@link MiniNode} against a collection, FHIRPath-style (every result is itself a collection). */
export function evaluateMiniFhirPath(node: MiniNode, collection: unknown[], env: MiniEnv): unknown[] {
  switch (node.k) {
    case 'lit':
      return [node.v];
    case 'var': {
      const value = env.vars[node.v];
      if (value === undefined) throw new Error(`Unknown variable %${node.v}`);
      return Array.isArray(value) ? value : [value];
    }
    case 'paren':
      return evaluateMiniFhirPath(node.e, collection, env);
    case 'path': {
      if (node.name === '$this') return collection;
      const out: unknown[] = [];
      for (const item of collection) {
        const record = item as Record<string, unknown> | null;
        if (record && typeof record === 'object' && record.resourceType === node.name) {
          out.push(item);
          continue;
        }
        getField(item, node.name, out);
      }
      return out;
    }
    case 'fn':
      return evalFunction(node, collection, env);
    case 'chain': {
      let current = collection;
      for (const part of node.parts) {
        if (part.k === 'index') {
          const index = evaluateMiniFhirPath(part.e, current, env)[0] as number;
          current = current[index] !== undefined ? [current[index]] : [];
        } else {
          current = evaluateMiniFhirPath(part, current, env);
        }
      }
      return current;
    }
    case 'bin': {
      const op = node.op;
      const left = evaluateMiniFhirPath(node.l, collection, env);
      const right = evaluateMiniFhirPath(node.r, collection, env);
      if (op === 'and') return [isTruthy(left) && isTruthy(right)];
      if (op === 'or') return [isTruthy(left) || isTruthy(right)];
      if (op === '|') {
        const out = [...left];
        for (const r of right) if (!out.some((x) => JSON.stringify(x) === JSON.stringify(r))) out.push(r);
        return out;
      }
      if (op === '&') {
        const l = left.length ? String(left[0]) : '';
        const r = right.length ? String(right[0]) : '';
        return [l + r];
      }
      if (!left.length || !right.length) return [];
      const l = left[0] as string | number;
      const r = right[0] as string | number;
      switch (op) {
        case '=':
          return [JSON.stringify(l) === JSON.stringify(r)];
        case '!=':
          return [JSON.stringify(l) !== JSON.stringify(r)];
        case '<':
          return [l < r];
        case '>':
          return [l > r];
        case '<=':
          return [l <= r];
        case '>=':
          return [l >= r];
        case '+':
          return [(l as number) + (r as number)];
        case '-':
          return [(l as number) - (r as number)];
        case '*':
          return [(l as number) * (r as number)];
        case '/':
          return [(l as number) / (r as number)];
        default:
          return [];
      }
    }
    default:
      return [];
  }
}
```

- [ ] **Step 2: Type-check**

Run:

```bash
cd frontend
npm run build
```

Expected: exits 0. (Not consumed yet — exercised by Task 5/6.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/benches/shared/miniFhirPath.ts
git commit -m "Add shared mini FHIRPath-subset engine for FML/SoF mock benches"
```

---

## Task 5: FML bench (mocked)

**Files:**
- Create: `frontend/src/benches/fml/fmlEngine.ts`
- Create: `frontend/src/benches/fml/fmlHighlight.ts`
- Create: `frontend/src/benches/fml/diffLines.ts`
- Create: `frontend/src/benches/fml/fmlFixtures.ts`
- Modify: `frontend/src/benches/fml/FmlBench.tsx` (replaces Task 1's placeholder)

**Interfaces:**
- Consumes: `parseMiniFhirPath`, `evaluateMiniFhirPath` from Task 4's `shared/miniFhirPath.ts`; `Card`, `ErrorBanner`, `Pills`, `PillItem`, style helpers from Task 1.
- Produces: `runFml(mapText, sourceJson): FmlRunResult`, `FmlLogRow`, `FmlRunResult` from `fmlEngine.ts`; `highlightFml(text): FmlHighlightLine[]` from `fmlHighlight.ts`; `diffLines(a, b): DiffRow[]` from `diffLines.ts`; `DEFAULT_MAP_TEXT`, `DEFAULT_SOURCE_TEXT`, `DEFAULT_EXPECTED_TEXT` from `fmlFixtures.ts`. All consumed only by `FmlBench.tsx`.

- [ ] **Step 1: Port the mockup's FML fixtures**

Create `frontend/src/benches/fml/fmlFixtures.ts`:

```ts
import { PATIENT_EXAMPLE } from '../fhirpath/sampleResources';

export const DEFAULT_MAP_TEXT = [
  'map "http://ignixa.dev/StructureMap/PatientToPerson" = "PatientToPerson"',
  '',
  'uses "http://hl7.org/fhir/StructureDefinition/Patient" alias Patient as source',
  'uses "http://hl7.org/fhir/StructureDefinition/Person" alias Person as target',
  '',
  'group PatientToPerson(source src : Patient, target tgt : Person) {',
  '  src.id as vId -> tgt.identifier = vId "copy_id";',
  '  src.gender as vG -> tgt.gender = vG "copy_gender";',
  '  src.birthDate as vB -> tgt.birthDate = vB "copy_birthDate";',
  '  src.active as vA -> tgt.active = vA "copy_active";',
  '  src.name.family as vF -> tgt.name.family = vF "map_family";',
  '  src.name.given as vGiv -> tgt.name.given = vGiv "map_given";',
  '  src.telecom.value as vT -> tgt.telecom.value = vT "map_telecom";',
  '  src.maritalStatus as vM -> tgt.maritalStatus = vM "copy_marital"; // absent in source',
  '}',
].join('\n');

export const DEFAULT_SOURCE_TEXT = JSON.stringify(PATIENT_EXAMPLE, null, 2);

export const DEFAULT_EXPECTED_TEXT = JSON.stringify(
  {
    resourceType: 'Person',
    identifier: [{ value: 'example' }],
    gender: 'male',
    birthDate: '1974-12-25',
    active: true,
    name: { family: ['Chalmers', 'Windsor'], given: ['Peter', 'James', 'Jim', 'Peter', 'James'] },
    telecom: { value: ['(03) 5555 6473', 'p.chalmers@example.org'] },
  },
  null,
  2,
);
```

- [ ] **Step 2: Port the LCS line-diff helper**

Create `frontend/src/benches/fml/diffLines.ts`:

```ts
export interface DiffRow {
  sign: ' ' | '−' | '+';
  text: string;
}

/** Longest-common-subsequence line diff between two texts, mockup-identical algorithm. */
export function diffLines(a: string, b: string): DiffRow[] {
  const linesA = a.split('\n');
  const linesB = b.split('\n');
  const n = linesA.length;
  const m = linesB.length;
  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i][j] = linesA[i] === linesB[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
    }
  }

  const rows: DiffRow[] = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (linesA[i] === linesB[j]) {
      rows.push({ sign: ' ', text: linesA[i] });
      i++;
      j++;
    } else if (dp[i + 1][j] >= dp[i][j + 1]) {
      rows.push({ sign: '−', text: linesA[i] });
      i++;
    } else {
      rows.push({ sign: '+', text: linesB[j] });
      j++;
    }
  }
  while (i < n) rows.push({ sign: '−', text: linesA[i++] });
  while (j < m) rows.push({ sign: '+', text: linesB[j++] });
  return rows;
}
```

- [ ] **Step 3: Port the FML syntax highlighter**

Create `frontend/src/benches/fml/fmlHighlight.ts`:

```ts
export interface FmlHighlightSegment {
  text: string;
  color: string;
}

export interface FmlHighlightLine {
  segments: FmlHighlightSegment[];
}

const KEYWORD_PATTERN =
  /^(map|uses|group|source|target|as|alias|imports|extends|where|then|first|not_first|last|not_last|only_one|share|collate|check|log|types|default)$/;

const TOKEN_PATTERN = /("[^"]*")|('[^']*')|(->)|([A-Za-z_][\w]*)|(\d+)|(\s+)|(.)/g;

/** Line-by-line syntax highlighter for the FML editor pane, mockup-identical rules. */
export function highlightFml(text: string): FmlHighlightLine[] {
  return text.split('\n').map((line) => {
    const segments: FmlHighlightSegment[] = [];
    let rest = line;
    let comment: string | null = null;
    const commentIndex = line.indexOf('//');
    if (commentIndex >= 0) {
      comment = line.slice(commentIndex);
      rest = line.slice(0, commentIndex);
    }

    const pattern = new RegExp(TOKEN_PATTERN);
    let match: RegExpExecArray | null;
    while ((match = pattern.exec(rest))) {
      const text2 = match[0];
      let color = 'var(--text2)';
      if (match[1]) color = 'var(--pass)';
      else if (match[2]) color = 'var(--chip-amb-fg)';
      else if (match[3]) color = 'var(--hl-arrow)';
      else if (match[4]) color = KEYWORD_PATTERN.test(text2) ? 'var(--accent)' : 'var(--text)';
      else if (match[5]) color = 'var(--chip-teal-fg)';
      segments.push({ text: text2, color });
    }

    if (comment) segments.push({ text: comment, color: 'var(--text4)' });
    if (segments.length === 0) segments.push({ text: ' ', color: 'var(--text2)' });
    return { segments };
  });
}
```

- [ ] **Step 4: Port the FML rule-application engine**

Create `frontend/src/benches/fml/fmlEngine.ts`:

```ts
import { evaluateMiniFhirPath, parseMiniFhirPath } from '../shared/miniFhirPath';

export type FmlRuleStatus = 'applied' | 'skipped' | 'error';

export interface FmlLogRow {
  rule: string;
  group: string;
  src: string;
  tgt: string;
  val: string;
  status: FmlRuleStatus;
}

export interface FmlRunResult {
  error: string | null;
  log: FmlLogRow[];
  output: Record<string, unknown> | null;
  mapName: string;
  applied: number;
  skipped: number;
}

function setPath(target: Record<string, unknown>, path: string, value: unknown): void {
  const parts = path.split('.');
  let cursor: Record<string, unknown> = target;
  for (let i = 0; i < parts.length - 1; i++) {
    const key = parts[i];
    if (typeof cursor[key] !== 'object' || cursor[key] === null) cursor[key] = {};
    cursor = cursor[key] as Record<string, unknown>;
  }
  cursor[parts[parts.length - 1]] = value;
}

const GROUP_PATTERN =
  /group\s+(\w+)\s*\(\s*source\s+(\w+)\s*(?::\s*(\w+))?\s*,\s*target\s+(\w+)\s*(?::\s*(\w+))?\s*\)\s*\{([\s\S]*?)\}/g;

const RULE_PATTERN = /^(\w+)(?:\.([\w.]+))?(?:\s+as\s+(\w+))?\s*->\s*(\w+)\.([\w.]+)(?:\s*=\s*(.+))?$/;

/** Applies a small StructureMap-subset (`group ... { rules }`) against a source resource, producing a target object and a per-rule execution log. Mockup-identical mini-interpreter. */
export function runFml(mapText: string, sourceText: string): FmlRunResult {
  let source: unknown;
  try {
    source = JSON.parse(sourceText);
  } catch (error) {
    return { error: `Source JSON — ${(error as Error).message}`, log: [], output: null, mapName: '', applied: 0, skipped: 0 };
  }

  const output: Record<string, unknown> = {};
  const log: FmlLogRow[] = [];
  let mapName = '(unnamed map)';
  let applied = 0;
  let skipped = 0;
  let foundGroup = false;

  const mapNameMatch = mapText.match(/map\s+"[^"]*"\s*=\s*"([^"]+)"/);
  if (mapNameMatch) mapName = mapNameMatch[1];

  try {
    const groupPattern = new RegExp(GROUP_PATTERN);
    let groupMatch: RegExpExecArray | null;
    while ((groupMatch = groupPattern.exec(mapText))) {
      foundGroup = true;
      const groupName = groupMatch[1];
      const sourceType = groupMatch[3] ?? 'src';
      const targetType = groupMatch[5] ?? 'tgt';
      const body = groupMatch[6];
      if (groupMatch[5] && !output.resourceType) output.resourceType = groupMatch[5];

      for (const rawLine of body.split('\n')) {
        const noComment = rawLine.replace(/\/\/.*$/, '').trim();
        if (!noComment || !noComment.endsWith(';')) continue;
        const line = noComment.slice(0, -1).trim();
        const ruleMatch = line.match(RULE_PATTERN);
        if (!ruleMatch) {
          log.push({ rule: '(parse)', group: groupName, src: line.slice(0, 40), tgt: '', val: '', status: 'error' });
          continue;
        }

        const [, , srcPath, alias, , tgtPath, rhsRaw] = ruleMatch;
        let rhs = (rhsRaw ?? '').trim();
        let ruleName = '(unnamed)';
        const ruleNameMatch = rhs.match(/"([^"]+)"\s*$/);
        if (ruleNameMatch) {
          ruleName = ruleNameMatch[1];
          rhs = rhs.slice(0, ruleNameMatch.index).trim();
        }

        let sourceValues: unknown[] = [source];
        if (srcPath) sourceValues = evaluateMiniFhirPath(parseMiniFhirPath(srcPath), [source], { vars: {} });

        let value: unknown;
        if (!rhs || rhs === alias) {
          value = sourceValues.length === 0 ? undefined : sourceValues.length === 1 ? sourceValues[0] : sourceValues;
        } else if (/^'.*'$/.test(rhs)) {
          value = rhs.slice(1, -1);
        } else {
          const truncateMatch = rhs.match(/^truncate\(\s*(\w+)\s*,\s*(\d+)\s*\)$/);
          if (truncateMatch && truncateMatch[1] === alias) {
            const first = sourceValues[0];
            value = first === undefined ? undefined : String(first).slice(0, Number.parseInt(truncateMatch[2], 10));
          } else {
            value = sourceValues.length ? (sourceValues.length === 1 ? sourceValues[0] : sourceValues) : undefined;
          }
        }

        const srcLabel = srcPath ? `${sourceType}.${srcPath}` : sourceType;
        const tgtLabel = `${targetType}.${tgtPath}`;
        if (value === undefined) {
          skipped++;
          log.push({ rule: ruleName, group: groupName, src: srcLabel, tgt: tgtLabel, val: '—', status: 'skipped' });
        } else {
          setPath(output, tgtPath, value);
          applied++;
          let valuePreview = JSON.stringify(value);
          if (valuePreview.length > 48) valuePreview = `${valuePreview.slice(0, 48)}…`;
          log.push({ rule: ruleName, group: groupName, src: srcLabel, tgt: tgtLabel, val: valuePreview, status: 'applied' });
        }
      }
    }
  } catch (error) {
    return { error: (error as Error).message, log, output, mapName, applied, skipped };
  }

  if (!foundGroup) {
    return {
      error: 'No group found. Expected: group Name(source src : Type, target tgt : Type) { … }',
      log: [],
      output: null,
      mapName,
      applied: 0,
      skipped: 0,
    };
  }

  return { error: null, log, output, mapName, applied, skipped };
}
```

- [ ] **Step 5: Write the FML bench UI**

Replace the contents of `frontend/src/benches/fml/FmlBench.tsx`:

```tsx
import { useMemo, useState, type CSSProperties } from 'react';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoTextareaStyle, primaryButtonStyle, sectionLabelStyle } from '../components/styles';
import { diffLines } from './diffLines';
import { runFml } from './fmlEngine';
import { DEFAULT_EXPECTED_TEXT, DEFAULT_MAP_TEXT, DEFAULT_SOURCE_TEXT } from './fmlFixtures';
import { highlightFml } from './fmlHighlight';

type FmlTab = 'output' | 'diff' | 'log';

const TAB_ITEMS: PillItem[] = [
  { id: 'output', label: 'Output' },
  { id: 'diff', label: 'Diff vs expected' },
  { id: 'log', label: 'Execution log' },
];

const twoColumnStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'minmax(420px,52%) 1fr',
  gap: 14,
  alignItems: 'start',
};

export function FmlBench() {
  const [mapText, setMapText] = useState(DEFAULT_MAP_TEXT);
  const [sourceText, setSourceText] = useState(DEFAULT_SOURCE_TEXT);
  const [expectedText, setExpectedText] = useState(DEFAULT_EXPECTED_TEXT);
  const [tab, setTab] = useState<FmlTab>('output');
  const [result, setResult] = useState(() => runFml(DEFAULT_MAP_TEXT, DEFAULT_SOURCE_TEXT));

  const highlightedLines = useMemo(() => highlightFml(mapText), [mapText]);
  const outputText = result.output ? JSON.stringify(result.output, null, 2) : '—';
  const diffRows = result.output ? diffLines(outputText, expectedText) : [];

  const runMap = () => setResult(runFml(mapText, sourceText));

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>FHIR Mapping Language</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>Author a StructureMap in FML and debug it rule by rule.</span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>mock engine · ignixa-fml 0.1</span>
        <button type="button" onClick={runMap} style={primaryButtonStyle}>
          ▶ Run map
        </button>
      </div>

      <div style={twoColumnStyle}>
        <Card>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Map source · .fml</span>
            <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>{result.mapName}</span>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 1, padding: '4px 0' }}>
            {highlightedLines.map((line, index) => (
              <div key={index} style={{ height: 19, whiteSpace: 'pre', fontFamily: monoFont, fontSize: 12 }}>
                {line.segments.map((segment, segmentIndex) => (
                  <span key={segmentIndex} style={{ color: segment.color, whiteSpace: 'pre' }}>
                    {segment.text}
                  </span>
                ))}
              </div>
            ))}
          </div>
          <textarea
            value={mapText}
            onChange={(event) => setMapText(event.target.value)}
            spellCheck={false}
            wrap="off"
            style={{ ...monoTextareaStyle, height: 300 }}
          />
        </Card>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <Card>
            <span style={sectionLabelStyle}>Source resource</span>
            <textarea
              value={sourceText}
              onChange={(event) => setSourceText(event.target.value)}
              spellCheck={false}
              style={{ ...monoTextareaStyle, height: 160, fontSize: 11.5 }}
            />
          </Card>

          <Card>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <Pills items={TAB_ITEMS} activeId={tab} onChange={(id) => setTab(id as FmlTab)} />
              <div style={{ flex: 1 }} />
              <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>
                {result.output ? `${result.applied} applied · ${result.skipped} skipped` : ''}
              </span>
            </div>

            {result.error ? <ErrorBanner message={result.error} /> : null}

            {!result.error && tab === 'output' ? (
              <pre
                style={{
                  margin: 0,
                  padding: '12px 14px',
                  borderRadius: 8,
                  background: 'var(--code)',
                  border: '1px solid var(--border)',
                  fontFamily: monoFont,
                  fontSize: 11.5,
                  lineHeight: 1.6,
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                  color: 'var(--text)',
                  maxHeight: 420,
                  overflow: 'auto',
                }}
              >
                {outputText}
              </pre>
            ) : null}

            {!result.error && tab === 'diff' ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                <textarea
                  value={expectedText}
                  onChange={(event) => setExpectedText(event.target.value)}
                  spellCheck={false}
                  style={{ ...monoTextareaStyle, height: 120, fontSize: 11 }}
                />
                <div style={{ border: '1px solid var(--border)', borderRadius: 8, overflow: 'auto', maxHeight: 300 }}>
                  {diffRows.map((row, index) => (
                    <div
                      key={index}
                      style={{
                        display: 'flex',
                        gap: 10,
                        padding: '1px 12px',
                        background: row.sign === '+' ? 'var(--pass-bg)' : row.sign === '−' ? 'var(--fail-bg)' : 'transparent',
                      }}
                    >
                      <span
                        style={{
                          fontFamily: monoFont,
                          fontSize: 11,
                          width: 10,
                          flex: 'none',
                          color: row.sign === '+' ? 'var(--pass)' : row.sign === '−' ? 'var(--fail)' : 'var(--chip-gray-fg)',
                        }}
                      >
                        {row.sign}
                      </span>
                      <span style={{ fontFamily: monoFont, fontSize: 11, whiteSpace: 'pre', color: 'var(--text2)' }}>{row.text}</span>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}

            {tab === 'log'
              ? result.log.map((row, index) => (
                  <div key={index} style={{ display: 'flex', gap: 12, alignItems: 'baseline', padding: '8px 10px', borderBottom: '1px solid var(--border)' }}>
                    <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)', width: 18, textAlign: 'right', flex: 'none' }}>{index + 1}</span>
                    <span style={{ fontFamily: monoFont, fontSize: 11.5, fontWeight: 600, color: 'var(--accent)', flex: 'none', minWidth: 110 }}>{row.rule}</span>
                    <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text2)', flex: 'none' }}>
                      {row.src} → {row.tgt}
                    </span>
                    <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)', flex: 1, minWidth: 0, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                      {row.val}
                    </span>
                    <span
                      style={{
                        fontFamily: monoFont,
                        fontSize: 9.5,
                        fontWeight: 600,
                        padding: '2px 8px',
                        borderRadius: 99,
                        flex: 'none',
                        background: row.status === 'applied' ? 'var(--pass-bg)' : row.status === 'skipped' ? 'var(--chip-gray-bg)' : 'var(--fail-bg)',
                        color: row.status === 'applied' ? 'var(--pass)' : row.status === 'skipped' ? 'var(--chip-gray-fg)' : 'var(--fail)',
                      }}
                    >
                      {row.status}
                    </span>
                  </div>
                ))
              : null}
          </Card>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 6: Type-check, lint, and build**

Run:

```bash
cd frontend
npm run build
npm run lint
```

Expected: both exit 0.

- [ ] **Step 7: Manual verification**

`npm run dev`, open `http://localhost:5173/benches.html`, FML tab:
- On load, the Output tab already shows the mapped `Person` resource (auto-run on mount) with 7 applied / 1 skipped (the commented-out `maritalStatus` rule).
- Editing the map text updates the syntax-highlighted overlay live; clicking "▶ Run map" re-evaluates.
- The Diff tab shows an empty diff (all matching lines) against the default expected fixture; editing the expected text shows `+`/`−` rows.
- The Execution log tab lists one row per rule with applied/skipped/error status chips.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/benches/fml
git commit -m "Add FML bench: mocked StructureMap-subset interpreter and UI"
```

---

## Task 6: SQL on FHIR bench (mocked)

**Files:**
- Create: `frontend/src/benches/sof/sofEngine.ts`
- Create: `frontend/src/benches/sof/sofFixtures.ts`
- Modify: `frontend/src/benches/sof/SofBench.tsx` (replaces Task 1's placeholder)

**Interfaces:**
- Consumes: `parseMiniFhirPath`, `evaluateMiniFhirPath` from Task 4's `shared/miniFhirPath.ts`; `Card`, `ErrorBanner`, style helpers from Task 1; `PATIENT_EXAMPLE` from Task 2's `fhirpath/sampleResources.ts`.
- Produces: `runSof(viewDefinitionJson, resourcesJson): SofRunResult` from `sofEngine.ts`; `DEFAULT_VIEW_DEFINITION_TEXT`, `DEFAULT_RESOURCES_TEXT` from `sofFixtures.ts`. Consumed only by `SofBench.tsx`.

- [ ] **Step 1: Port the mockup's ViewDefinition and resource fixtures**

Create `frontend/src/benches/sof/sofFixtures.ts`:

```ts
import { PATIENT_EXAMPLE } from '../fhirpath/sampleResources';

export const DEFAULT_VIEW_DEFINITION_TEXT = JSON.stringify(
  {
    resource: 'Patient',
    status: 'active',
    name: 'patient_demographics',
    select: [
      {
        column: [
          { name: 'id', path: 'id' },
          { name: 'gender', path: 'gender' },
          { name: 'birth_date', path: 'birthDate' },
        ],
      },
      {
        forEach: "name.where(use = 'official')",
        column: [
          { name: 'family', path: 'family' },
          { name: 'given', path: "given.join(' ')" },
        ],
      },
    ],
  },
  null,
  2,
);

const AMY: Record<string, unknown> = {
  resourceType: 'Patient',
  id: 'pt-amy',
  active: true,
  gender: 'female',
  birthDate: '1987-02-20',
  name: [{ use: 'official', family: 'Shaw', given: ['Amy', 'V.'] }],
  telecom: [{ system: 'email', value: 'amy.shaw@example.org' }],
};

const RI: Record<string, unknown> = {
  resourceType: 'Patient',
  id: 'pt-anon',
  active: false,
  gender: 'other',
  birthDate: '1990-07-31',
  name: [{ use: 'usual', given: ['Ri'] }],
};

export const DEFAULT_RESOURCES_TEXT = JSON.stringify([PATIENT_EXAMPLE, AMY, RI], null, 2);
```

- [ ] **Step 2: Port the ViewDefinition flattening engine**

Create `frontend/src/benches/sof/sofEngine.ts`:

```ts
import { evaluateMiniFhirPath, parseMiniFhirPath } from '../shared/miniFhirPath';

interface SofColumn {
  name: string;
  path: string;
}

interface SofSelect {
  forEach?: string;
  forEachOrNull?: string;
  column?: SofColumn[];
  select?: SofSelect[];
}

interface SofViewDefinition {
  resource?: string;
  select?: SofSelect[];
}

export type SofCellValue = string | number | boolean | null;

export interface SofRunResult {
  error: string | null;
  columns: string[];
  rows: Record<string, SofCellValue>[];
  meta: string;
}

function toCellValue(values: unknown[]): SofCellValue {
  if (values.length === 0) return null;
  const value = values.length === 1 ? values[0] : values;
  if (typeof value === 'object' && value !== null) return JSON.stringify(value);
  return value as SofCellValue;
}

function selectRows(selects: SofSelect[], context: unknown): Record<string, SofCellValue>[] {
  let rows: Record<string, SofCellValue>[] = [{}];

  for (const part of selects) {
    let contexts: unknown[];
    if (part.forEach !== undefined) {
      contexts = evaluateMiniFhirPath(parseMiniFhirPath(part.forEach), [context], { vars: {} });
    } else if (part.forEachOrNull !== undefined) {
      contexts = evaluateMiniFhirPath(parseMiniFhirPath(part.forEachOrNull), [context], { vars: {} });
      if (contexts.length === 0) contexts = [null];
    } else {
      contexts = [context];
    }

    const partRows: Record<string, SofCellValue>[] = [];
    for (const item of contexts) {
      const base: Record<string, SofCellValue> = {};
      for (const column of part.column ?? []) {
        if (item === null) {
          base[column.name] = null;
          continue;
        }
        base[column.name] = toCellValue(evaluateMiniFhirPath(parseMiniFhirPath(column.path), [item], { vars: {} }));
      }
      if (part.select && item !== null) {
        for (const nested of selectRows(part.select, item)) partRows.push({ ...base, ...nested });
      } else {
        partRows.push(base);
      }
    }

    const next: Record<string, SofCellValue>[] = [];
    for (const existingRow of rows) for (const partRow of partRows) next.push({ ...existingRow, ...partRow });
    rows = next;
  }

  return rows;
}

function collectColumnNames(selects: SofSelect[] | undefined, out: string[]): void {
  for (const part of selects ?? []) {
    for (const column of part.column ?? []) if (!out.includes(column.name)) out.push(column.name);
    if (part.select) collectColumnNames(part.select, out);
  }
}

/** Flattens `resources` through a ViewDefinition's `select`/`forEach`/`column` tree, mockup-identical engine. */
export function runSof(viewDefinitionText: string, resourcesText: string): SofRunResult {
  let viewDefinition: SofViewDefinition;
  let resources: unknown;
  try {
    viewDefinition = JSON.parse(viewDefinitionText) as SofViewDefinition;
  } catch (error) {
    return { error: `ViewDefinition JSON — ${(error as Error).message}`, columns: [], rows: [], meta: '' };
  }
  try {
    resources = JSON.parse(resourcesText);
  } catch (error) {
    return { error: `Resources JSON — ${(error as Error).message}`, columns: [], rows: [], meta: '' };
  }

  const resourceList = Array.isArray(resources) ? resources : [resources];
  const columns: string[] = [];
  collectColumnNames(viewDefinition.select, columns);

  const rows: Record<string, SofCellValue>[] = [];
  let scanned = 0;
  try {
    for (const resource of resourceList) {
      const record = resource as { resourceType?: string };
      if (viewDefinition.resource && record.resourceType !== viewDefinition.resource) continue;
      scanned++;
      rows.push(...selectRows(viewDefinition.select ?? [], resource));
    }
  } catch (error) {
    return { error: (error as Error).message, columns, rows: [], meta: '' };
  }

  const meta = `${rows.length} ${rows.length === 1 ? 'row' : 'rows'} · ${columns.length} cols · ${scanned} resources`;
  return { error: null, columns, rows, meta };
}
```

- [ ] **Step 3: Write the SQL on FHIR bench UI**

Replace the contents of `frontend/src/benches/sof/SofBench.tsx`:

```tsx
import { useState, type CSSProperties } from 'react';
import { Card, ErrorBanner } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoTextareaStyle, primaryButtonStyle, sectionLabelStyle } from '../components/styles';
import { runSof } from './sofEngine';
import { DEFAULT_RESOURCES_TEXT, DEFAULT_VIEW_DEFINITION_TEXT } from './sofFixtures';

const twoColumnStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'minmax(380px,44%) 1fr',
  gap: 14,
  alignItems: 'start',
};

export function SofBench() {
  const [viewDefinitionText, setViewDefinitionText] = useState(DEFAULT_VIEW_DEFINITION_TEXT);
  const [resourcesText, setResourcesText] = useState(DEFAULT_RESOURCES_TEXT);
  const [result, setResult] = useState(() => runSof(DEFAULT_VIEW_DEFINITION_TEXT, DEFAULT_RESOURCES_TEXT));

  const runView = () => setResult(runSof(viewDefinitionText, resourcesText));
  const gridColumns = result.columns.length ? `repeat(${result.columns.length}, minmax(110px, 1fr))` : '1fr';

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>SQL on FHIR</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>Run a ViewDefinition over resources and inspect the flattened table.</span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>mock engine · ignixa-views 0.1</span>
        <button type="button" onClick={runView} style={primaryButtonStyle}>
          ▶ Run view
        </button>
      </div>

      <div style={twoColumnStyle}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <Card>
            <span style={sectionLabelStyle}>ViewDefinition</span>
            <textarea
              value={viewDefinitionText}
              onChange={(event) => setViewDefinitionText(event.target.value)}
              spellCheck={false}
              style={{ ...monoTextareaStyle, height: 320, fontSize: 11.5 }}
            />
          </Card>
          <Card>
            <span style={sectionLabelStyle}>Resources · JSON array</span>
            <textarea
              value={resourcesText}
              onChange={(event) => setResourcesText(event.target.value)}
              spellCheck={false}
              style={{ ...monoTextareaStyle, height: 220, fontSize: 11.5 }}
            />
          </Card>
        </div>

        <Card style={{ minHeight: 400 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Result table</span>
            <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>{result.meta}</span>
          </div>

          {result.error ? <ErrorBanner message={result.error} /> : null}

          <div style={{ border: '1px solid var(--border)', borderRadius: 8, overflow: 'auto' }}>
            <div style={{ display: 'grid', gridTemplateColumns: gridColumns, background: 'var(--panel2)', borderBottom: '1px solid var(--border)' }}>
              {result.columns.map((column) => (
                <span
                  key={column}
                  style={{
                    padding: '8px 12px',
                    fontFamily: monoFont,
                    fontSize: 10,
                    fontWeight: 600,
                    letterSpacing: '.08em',
                    textTransform: 'uppercase',
                    color: 'var(--text2)',
                    borderRight: '1px solid var(--border)',
                  }}
                >
                  {column}
                </span>
              ))}
            </div>
            {result.rows.map((row, rowIndex) => (
              <div key={rowIndex} style={{ display: 'grid', gridTemplateColumns: gridColumns, borderBottom: '1px solid var(--border)' }}>
                {result.columns.map((column) => {
                  const value = row[column];
                  return (
                    <span
                      key={column}
                      style={{
                        padding: '7px 12px',
                        fontFamily: monoFont,
                        fontSize: 11.5,
                        color: value === null ? 'var(--text4)' : 'var(--text)',
                        borderRight: '1px solid var(--border)',
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                      }}
                    >
                      {value === null ? '∅' : String(value)}
                    </span>
                  );
                })}
              </div>
            ))}
          </div>
          <span style={{ fontSize: 11, color: 'var(--text4)' }}>
            Columns come from <span style={{ fontFamily: monoFont }}>select[].column[].path</span> (FHIRPath);{' '}
            <span style={{ fontFamily: monoFont }}>forEach</span> unnests one row per item.
          </span>
        </Card>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Type-check, lint, and build**

Run:

```bash
cd frontend
npm run build
npm run lint
```

Expected: both exit 0.

- [ ] **Step 5: Manual verification**

`npm run dev`, open `http://localhost:5173/benches.html`, SQL on FHIR tab:
- On load, the result table already shows 3 rows (one official name row for the default Patient, one row each for Amy and Ri) across columns `id`/`gender`/`birth_date`/`family`/`given`, with `∅` for Ri's missing `family` (no official name).
- Editing the ViewDefinition or resources JSON and clicking "▶ Run view" recomputes the table.
- Malformed JSON in either textarea shows the red error banner instead of a stale table.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/benches/sof
git commit -m "Add SQL on FHIR bench: mocked ViewDefinition flattening engine and UI"
```

---

## Task 7: Final verification pass

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: the whole feature built in Tasks 1-6.
- Produces: nothing new — this task only verifies and documents.

- [ ] **Step 1: Update the README's frontend description**

Open `README.md`. In the `## Repository layout` code block, change:

```
├── frontend/                    Vite + React 19 + TypeScript SPA
│   └── src/                      api client, types, hooks, components
```

to:

```
├── frontend/                    Vite + React 19 + TypeScript, two pages
│   └── src/                      conformance app (api client, types, hooks,
│                                  components) + benches/ (Expression Benches:
│                                  FHIRPath [real backend], FML/SQL-on-FHIR [mocked])
```

In the `## Quick start` section, after the existing:

```
The dev server proxies `/api/*` to the Functions host on port 7071, so the SPA
works against the local backend with no extra configuration.
```

add a new paragraph:

```
The frontend is a two-page build: `/` is the TestScript conformance runner,
`/benches.html` is Expression Benches (FHIRPath, FML, SQL on FHIR). Both are
served by the same dev server and cross-link to each other in their top bars.
```

- [ ] **Step 2: Full build and lint, both entries**

Run:

```bash
cd frontend
npm run build
npm run lint
```

Expected: both exit 0. Confirm `frontend/dist/index.html` and `frontend/dist/benches.html` both exist after the build.

- [ ] **Step 3: Backend regression check**

Run the existing backend test suite to confirm nothing here inadvertently touched backend behavior (this plan doesn't modify backend code, so this should be a no-op check):

```bash
dotnet test Ignixa.Lab.sln
```

Expected: exits 0, same pass count as before this feature branch.

- [ ] **Step 4: Full manual walkthrough**

With `func start` (backend) and `npm run dev` (frontend) both running:
- Light theme: visit `/`, confirm the "Expression Benches ↗" link works; visit `/benches.html`, confirm "Conformance ↗" returns to `/`.
- Toggle dark theme from either page; confirm it persists across a reload and applies consistently on both pages (shared `localStorage` key).
- FHIRPath tab: run at least one real expression against each of the 5 FHIR version routes (STU3/R4/R4B/R5/R6) and confirm each returns a 200 with parsed results (watch the Network tab or the Functions host console log for the request path).
- FML tab: run the default map, confirm output/diff/log tabs all populate.
- SQL on FHIR tab: run the default view, confirm the table populates.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "Document the Expression Benches page in the README"
```
