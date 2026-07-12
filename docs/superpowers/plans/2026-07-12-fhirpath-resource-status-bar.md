# FHIRPath Resource Status Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persistent status bar beneath the FHIRPath test-resource editor that resolves clicked JSON keys and values to exact indexed FHIRPath expressions and copies them with explicit feedback.

**Architecture:** Keep the existing layered textarea and report pointer-completed source offsets to `FhirPathBench`. A pure recursive JSON source parser maps offsets to key/value spans and exact FHIRPath strings; a focused status-bar component presents the result and reuses the clipboard hook, extended with an explicit failure state.

**Tech Stack:** React 19, TypeScript 6, Node 24 built-in test runner, Playwright, Vite 8, oxlint.

---

## File Map

- Create `frontend/src/benches/fhirpath/jsonPathResolver.ts` for dependency-free source-offset parsing and FHIRPath formatting.
- Create `frontend/src/benches/fhirpath/jsonPathResolver.test.ts` for resolver unit coverage.
- Create `frontend/src/benches/fhirpath/ResourcePathStatusBar.tsx` for the path display, copy icon, and copy feedback.
- Create `frontend/src/hooks/useCopyToClipboard.test.ts` for clipboard-status state coverage.
- Modify `frontend/src/hooks/useCopyToClipboard.ts` to expose explicit copy failure state without breaking current callers.
- Modify `frontend/src/benches/components/HighlightedTextarea.tsx` to expose an accessible label and pointer-selection callback.
- Modify `frontend/src/benches/fhirpath/FhirPathBench.tsx` to resolve selections, clear stale state, and compose the status bar.
- Modify `frontend/scripts/responsive-check.mjs` to exercise selection, clearing, invalid JSON, and narrow layouts.
- Modify `frontend/package.json` to add the dependency-free unit-test command.

### Task 1: Resolve JSON source offsets to exact FHIRPath

**Files:**
- Create: `frontend/src/benches/fhirpath/jsonPathResolver.test.ts`
- Create: `frontend/src/benches/fhirpath/jsonPathResolver.ts`
- Modify: `frontend/package.json:6-11`

- [ ] **Step 1: Add the unit-test command and write the failing resolver tests**

Add this script to `frontend/package.json`:

```json
"test": "node --experimental-strip-types --test src/benches/fhirpath/jsonPathResolver.test.ts",
```

Create `frontend/src/benches/fhirpath/jsonPathResolver.test.ts`:

```ts
/// <reference types="node" />

import assert from 'node:assert/strict';
import test from 'node:test';
import { resolveJsonPathAtOffset } from './jsonPathResolver.ts';

function offsetOf(source: string, token: string, occurrence = 0): number {
  let offset = -1;
  for (let index = 0; index <= occurrence; index += 1) {
    offset = source.indexOf(token, offset + 1);
  }
  assert.notEqual(offset, -1, `Expected ${JSON.stringify(token)} in source`);
  return offset;
}

const patient = `{
  "resourceType": "Patient",
  "name": [
    {
      "given": ["Peter", "James"],
      "family": "Chalmers"
    }
  ]
}`;

test('resolves a property key and its scalar value to the same path', () => {
  assert.deepEqual(resolveJsonPathAtOffset(patient, offsetOf(patient, '"family"') + 2), {
    kind: 'match',
    selection: {
      path: 'name[0].family',
      start: offsetOf(patient, '"family"'),
      end: offsetOf(patient, '"family"') + '"family"'.length,
    },
  });
  assert.equal(
    resolveJsonPathAtOffset(patient, offsetOf(patient, '"Chalmers"') + 2).kind,
    'match',
  );
  const valueResult = resolveJsonPathAtOffset(patient, offsetOf(patient, '"Chalmers"') + 2);
  assert.equal(valueResult.kind === 'match' ? valueResult.selection.path : null, 'name[0].family');
});

test('includes exact indexes for primitive array items', () => {
  const result = resolveJsonPathAtOffset(patient, offsetOf(patient, '"James"') + 2);
  assert.equal(result.kind === 'match' ? result.selection.path : null, 'name[0].given[1]');
});

test('resolves an object-valued property key', () => {
  const result = resolveJsonPathAtOffset(patient, offsetOf(patient, '"name"') + 2);
  assert.equal(result.kind === 'match' ? result.selection.path : null, 'name');
});

test('escapes property names that are not bare FHIRPath identifiers', () => {
  const source = '{"value-set":{"a`b":true}}';
  const result = resolveJsonPathAtOffset(source, offsetOf(source, 'true') + 1);
  assert.equal(result.kind === 'match' ? result.selection.path : null, '`value-set`.`a\\`b`');
});

test('returns none for whitespace, punctuation, and token end boundaries', () => {
  assert.deepEqual(resolveJsonPathAtOffset(patient, patient.indexOf('\n')), { kind: 'none' });
  assert.deepEqual(resolveJsonPathAtOffset(patient, patient.indexOf('{')), { kind: 'none' });
  const familyEnd = offsetOf(patient, '"family"') + '"family"'.length;
  assert.deepEqual(resolveJsonPathAtOffset(patient, familyEnd), { kind: 'none' });
});

test('returns invalid instead of guessing from malformed JSON', () => {
  assert.deepEqual(resolveJsonPathAtOffset('{"name": [', 2), { kind: 'invalid' });
});

test('returns none for offsets outside the source', () => {
  assert.deepEqual(resolveJsonPathAtOffset(patient, -1), { kind: 'none' });
  assert.deepEqual(resolveJsonPathAtOffset(patient, patient.length), { kind: 'none' });
});
```

- [ ] **Step 2: Run the resolver tests and confirm the expected failure**

Run:

```powershell
Set-Location frontend
npm test
```

Expected: FAIL with `ERR_MODULE_NOT_FOUND` for `jsonPathResolver.ts`.

- [ ] **Step 3: Implement the minimal source parser and formatter**

Create `frontend/src/benches/fhirpath/jsonPathResolver.ts`:

```ts
export interface JsonPathSelection {
  path: string;
  start: number;
  end: number;
}

export type JsonPathResolution =
  | { kind: 'match'; selection: JsonPathSelection }
  | { kind: 'none' }
  | { kind: 'invalid' };

const BARE_IDENTIFIER = /^[A-Za-z_][A-Za-z0-9_]*$/;

export function resolveJsonPathAtOffset(source: string, offset: number): JsonPathResolution {
  if (offset < 0 || offset >= source.length) {
    return { kind: 'none' };
  }

  try {
    JSON.parse(source);
  } catch {
    return { kind: 'invalid' };
  }

  const parser = new JsonSourceParser(source);
  parser.parse();
  const selection = parser.selections
    .filter((candidate) => offset >= candidate.start && offset < candidate.end)
    .sort((left, right) => (left.end - left.start) - (right.end - right.start))[0];

  return selection ? { kind: 'match', selection } : { kind: 'none' };
}

class JsonSourceParser {
  readonly selections: JsonPathSelection[] = [];
  private index = 0;

  constructor(private readonly source: string) {}

  parse(): void {
    this.parseValue('');
    this.skipWhitespace();
  }

  private parseValue(path: string): void {
    this.skipWhitespace();
    const current = this.source[this.index];
    if (current === '{') {
      this.parseObject(path);
      return;
    }
    if (current === '[') {
      this.parseArray(path);
      return;
    }

    const span = current === '"' ? this.readStringSpan() : this.readPrimitiveSpan();
    if (path) {
      this.selections.push({ path, ...span });
    }
  }

  private parseObject(path: string): void {
    this.index += 1;
    this.skipWhitespace();
    if (this.source[this.index] === '}') {
      this.index += 1;
      return;
    }

    while (this.index < this.source.length) {
      this.skipWhitespace();
      const keySpan = this.readStringSpan();
      const propertyName = JSON.parse(this.source.slice(keySpan.start, keySpan.end)) as string;
      const propertyPath = appendProperty(path, propertyName);
      this.selections.push({ path: propertyPath, ...keySpan });

      this.skipWhitespace();
      this.expect(':');
      this.parseValue(propertyPath);
      this.skipWhitespace();
      if (this.source[this.index] === '}') {
        this.index += 1;
        return;
      }
      this.expect(',');
    }
  }

  private parseArray(path: string): void {
    this.index += 1;
    this.skipWhitespace();
    if (this.source[this.index] === ']') {
      this.index += 1;
      return;
    }

    let itemIndex = 0;
    while (this.index < this.source.length) {
      const itemPath = `${path}[${itemIndex}]`;
      this.parseValue(itemPath);
      itemIndex += 1;
      this.skipWhitespace();
      if (this.source[this.index] === ']') {
        this.index += 1;
        return;
      }
      this.expect(',');
    }
  }

  private readStringSpan(): { start: number; end: number } {
    const start = this.index;
    this.expect('"');
    while (this.index < this.source.length) {
      if (this.source[this.index] === '\\') {
        this.index += 2;
      } else if (this.source[this.index] === '"') {
        this.index += 1;
        return { start, end: this.index };
      } else {
        this.index += 1;
      }
    }
    return { start, end: this.index };
  }

  private readPrimitiveSpan(): { start: number; end: number } {
    const start = this.index;
    while (this.index < this.source.length && !/[\s,\]}]/.test(this.source[this.index])) {
      this.index += 1;
    }
    return { start, end: this.index };
  }

  private skipWhitespace(): void {
    while (this.index < this.source.length && /\s/.test(this.source[this.index])) {
      this.index += 1;
    }
  }

  private expect(character: string): void {
    if (this.source[this.index] !== character) {
      throw new Error(`Expected ${character} at offset ${this.index}`);
    }
    this.index += 1;
  }
}

function appendProperty(path: string, propertyName: string): string {
  const segment = BARE_IDENTIFIER.test(propertyName)
    ? propertyName
    : `\`${propertyName.replace(/\\/g, '\\\\').replace(/`/g, '\\`')}\``;
  return path ? `${path}.${segment}` : segment;
}
```

- [ ] **Step 4: Run the resolver tests and frontend static checks**

Run:

```powershell
Set-Location frontend
npm test
npm run lint
npm run build
```

Expected: all resolver tests PASS, lint exits 0, and Vite reports a successful production build.

- [ ] **Step 5: Commit the resolver**

```powershell
git add frontend\package.json frontend\src\benches\fhirpath\jsonPathResolver.ts frontend\src\benches\fhirpath\jsonPathResolver.test.ts
git commit -m "Add JSON to FHIRPath resolver" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`nCopilot-Session: 7415203e-751e-462d-b71e-594a55a0dfd7"
```

### Task 2: Surface clipboard success and failure

**Files:**
- Create: `frontend/src/hooks/useCopyToClipboard.test.ts`
- Modify: `frontend/src/hooks/useCopyToClipboard.ts:1-30`
- Modify: `frontend/package.json:6-12`

- [ ] **Step 1: Write the failing clipboard-state test and include it in the test command**

Change the test script in `frontend/package.json` to:

```json
"test": "node --experimental-strip-types --test src/benches/fhirpath/jsonPathResolver.test.ts src/hooks/useCopyToClipboard.test.ts",
```

Create `frontend/src/hooks/useCopyToClipboard.test.ts`:

```ts
/// <reference types="node" />

import assert from 'node:assert/strict';
import test from 'node:test';
import { copyStatusReducer } from './useCopyToClipboard.ts';

test('tracks copied, failed, and reset clipboard states', () => {
  assert.equal(copyStatusReducer('idle', { type: 'copied' }), 'copied');
  assert.equal(copyStatusReducer('idle', { type: 'failed' }), 'failed');
  assert.equal(copyStatusReducer('failed', { type: 'reset' }), 'idle');
  assert.equal(copyStatusReducer('copied', { type: 'reset' }), 'idle');
});
```

- [ ] **Step 2: Run the tests and confirm the expected failure**

Run:

```powershell
Set-Location frontend
npm test
```

Expected: FAIL because `copyStatusReducer` is not exported.

- [ ] **Step 3: Extend the clipboard hook without breaking existing consumers**

Replace `frontend/src/hooks/useCopyToClipboard.ts` with:

```ts
import { useEffect, useReducer, useRef } from 'react';

export type CopyStatus = 'idle' | 'copied' | 'failed';
export type CopyStatusEvent = { type: 'copied' | 'failed' | 'reset' };

export function copyStatusReducer(_status: CopyStatus, event: CopyStatusEvent): CopyStatus {
  switch (event.type) {
    case 'copied':
      return 'copied';
    case 'failed':
      return 'failed';
    case 'reset':
      return 'idle';
  }
}

/** Copies `text` and reports temporary success or failure feedback for `durationMs`. */
export function useCopyToClipboard(
  text: string,
  durationMs: number,
): { copied: boolean; failed: boolean; copy: () => void } {
  const [status, dispatch] = useReducer(copyStatusReducer, 'idle');
  const timeoutRef = useRef<number | null>(null);

  const clearResetTimer = () => {
    if (timeoutRef.current !== null) {
      window.clearTimeout(timeoutRef.current);
      timeoutRef.current = null;
    }
  };

  const showTemporaryStatus = (type: 'copied' | 'failed') => {
    clearResetTimer();
    dispatch({ type });
    timeoutRef.current = window.setTimeout(() => dispatch({ type: 'reset' }), durationMs);
  };

  useEffect(() => {
    clearResetTimer();
    dispatch({ type: 'reset' });
  }, [text]);

  useEffect(() => () => clearResetTimer(), []);

  const copy = () => {
    if (!navigator.clipboard) {
      showTemporaryStatus('failed');
      return;
    }
    void navigator.clipboard.writeText(text).then(
      () => showTemporaryStatus('copied'),
      () => showTemporaryStatus('failed'),
    );
  };

  return { copied: status === 'copied', failed: status === 'failed', copy };
}
```

- [ ] **Step 4: Run the focused and regression checks**

Run:

```powershell
Set-Location frontend
npm test
npm run lint
npm run build
```

Expected: all tests PASS; existing destructuring callers continue to type-check; lint and build exit 0.

- [ ] **Step 5: Commit clipboard feedback**

```powershell
git add frontend\package.json frontend\src\hooks\useCopyToClipboard.ts frontend\src\hooks\useCopyToClipboard.test.ts
git commit -m "Report clipboard copy failures" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`nCopilot-Session: 7415203e-751e-462d-b71e-594a55a0dfd7"
```

### Task 3: Integrate selection and the status bar

**Files:**
- Create: `frontend/src/benches/fhirpath/ResourcePathStatusBar.tsx`
- Modify: `frontend/src/benches/components/HighlightedTextarea.tsx:13-26,52-54,97-120`
- Modify: `frontend/src/benches/fhirpath/FhirPathBench.tsx:1-174,310-363`
- Modify: `frontend/scripts/responsive-check.mjs:64-79`

- [ ] **Step 1: Add a failing browser-level interaction check**

Call `assertFhirPathResourcePath(page)` after line 70 of the Benches route check in `frontend/scripts/responsive-check.mjs`:

```js
await assertNoHorizontalOverflow(page, 'Benches FHIRPath');
await assertFhirPathResourcePath(page);
```

Add this helper before the fixture constants:

```js
async function assertFhirPathResourcePath(page) {
  const editor = page.getByRole('textbox', { name: 'Test resource JSON' });
  await editor.waitFor();
  const originalSource = await editor.inputValue();

  await editor.evaluate((element) => {
    const textarea = element;
    const offset = textarea.value.indexOf('"Peter"') + 2;
    textarea.focus();
    textarea.setSelectionRange(offset, offset);
    textarea.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
  });

  await expectVisible(
    page.getByText('name[0].given[0]', { exact: true }),
    'FHIRPath resource selection path',
  );
  await expectVisible(
    page.getByRole('button', { name: 'Copy FHIRPath' }),
    'FHIRPath resource copy action',
  );

  await editor.fill(`${originalSource}\n`);
  await expectVisible(
    page.getByText('Click a JSON key or value', { exact: true }),
    'FHIRPath resource cleared path',
  );

  await editor.fill('{');
  await editor.evaluate((element) => {
    element.setSelectionRange(0, 0);
    element.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
  });
  await expectVisible(
    page.getByText('Fix JSON to inspect a path', { exact: true }),
    'FHIRPath resource invalid JSON path state',
  );

  await editor.fill(originalSource);
}
```

- [ ] **Step 2: Run the browser check and confirm the expected failure**

Run:

```powershell
Set-Location frontend
npm run responsive:check
```

Expected: FAIL because no textbox named `Test resource JSON` exists yet.

- [ ] **Step 3: Expose the textarea interaction from `HighlightedTextarea`**

Add these props to `HighlightedTextareaProps`:

```ts
ariaLabel?: string;
onPointerSelection?: (textarea: HTMLTextAreaElement) => void;
```

Destructure them in the component:

```ts
{ value, onChange, lines, style, spellCheck = false, autoGrowMaxHeight, ariaLabel, onPointerSelection }
```

Add these attributes to the underlying `<textarea>`:

```tsx
aria-label={ariaLabel}
onPointerUp={(event) => onPointerSelection?.(event.currentTarget)}
```

- [ ] **Step 4: Create the focused status-bar component**

Create `frontend/src/benches/fhirpath/ResourcePathStatusBar.tsx`:

```tsx
import { useCopyToClipboard } from '../../hooks/useCopyToClipboard';
import { COPY_FEEDBACK_DURATION_MS } from '../../lib/shareLinks';
import { monoFont } from '../components/styles';

export interface ResourcePathStatusBarProps {
  path: string | null;
  invalid: boolean;
}

function CopyIcon({ copied }: { copied: boolean }) {
  return copied ? (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="m5 12 4 4L19 6" />
    </svg>
  ) : (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect width="14" height="14" x="8" y="8" rx="2" />
      <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" />
    </svg>
  );
}

export function ResourcePathStatusBar({ path, invalid }: ResourcePathStatusBarProps) {
  const { copied, failed, copy } = useCopyToClipboard(path ?? '', COPY_FEEDBACK_DURATION_MS);
  const displayText = invalid ? 'Fix JSON to inspect a path' : path ?? 'Click a JSON key or value';
  const feedback = copied ? 'Copied' : failed ? 'Copy failed' : '';

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 10,
        minHeight: 38,
        padding: '0 8px 0 13px',
        border: '1px solid var(--border2)',
        borderTop: 'none',
        borderRadius: '0 0 8px 8px',
        background: 'var(--inset)',
        fontFamily: monoFont,
        minWidth: 0,
      }}
    >
      <span style={{ fontSize: 9, letterSpacing: '.13em', textTransform: 'uppercase', color: 'var(--text4)', flex: 'none' }}>
        Path
      </span>
      <span
        title={path ?? undefined}
        style={{
          minWidth: 0,
          flex: 1,
          color: path ? 'var(--accent)' : invalid ? 'var(--fail)' : 'var(--text4)',
          fontSize: 11.5,
          whiteSpace: 'nowrap',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          userSelect: 'text',
        }}
      >
        {displayText}
      </span>
      <span
        role="status"
        aria-live="polite"
        style={{ minWidth: feedback ? 58 : 0, fontSize: 10, color: failed ? 'var(--fail)' : 'var(--text3)', flex: 'none' }}
      >
        {feedback}
      </span>
      <button
        type="button"
        onClick={copy}
        disabled={!path}
        aria-label="Copy FHIRPath"
        title={copied ? 'Copied' : failed ? 'Copy failed' : 'Copy FHIRPath'}
        style={{
          width: 30,
          height: 30,
          display: 'grid',
          placeItems: 'center',
          flex: 'none',
          border: '1px solid var(--border2)',
          borderRadius: 6,
          background: 'var(--panel)',
          color: copied ? 'var(--pass)' : 'var(--text2)',
          padding: 0,
          cursor: path ? 'pointer' : 'not-allowed',
          opacity: path ? 1 : 0.45,
        }}
      >
        <CopyIcon copied={copied} />
      </button>
    </div>
  );
}
```

- [ ] **Step 5: Wire source resolution into `FhirPathBench`**

Add imports:

```ts
import { ResourcePathStatusBar } from './ResourcePathStatusBar';
import { resolveJsonPathAtOffset, type JsonPathSelection } from './jsonPathResolver';
```

Add state beside the existing textarea ref:

```ts
const resourceRef = useRef<HTMLTextAreaElement>(null);
const [selectedResourcePath, setSelectedResourcePath] = useState<JsonPathSelection | null>(null);
const [resourcePathInvalid, setResourcePathInvalid] = useState(false);
```

Add these handlers after `removeVariable`:

```ts
const clearResourcePath = () => {
  setSelectedResourcePath(null);
  setResourcePathInvalid(false);
};

const handleResourceTextChange = (nextText: string) => {
  setResourceText(nextText);
  clearResourcePath();
};

const handleResourcePointerSelection = (textarea: HTMLTextAreaElement) => {
  const resolution = resolveJsonPathAtOffset(resourceText, textarea.selectionStart);
  if (resolution.kind === 'invalid') {
    setSelectedResourcePath(null);
    setResourcePathInvalid(true);
    return;
  }
  if (resolution.kind === 'none') {
    return;
  }

  setResourcePathInvalid(false);
  setSelectedResourcePath(resolution.selection);
  textarea.focus();
  textarea.setSelectionRange(resolution.selection.start, resolution.selection.end);
};
```

In the fakes-seed effect, clear both path states immediately after setting the
new resource:

```ts
setSelectedResourcePath(null);
setResourcePathInvalid(false);
```

In each sample-resource button handler, replace `setResourceText(...)` with:

```ts
handleResourceTextChange(JSON.stringify(sample.data, null, 2));
```

Replace the resource editor with:

```tsx
<HighlightedTextarea
  ref={resourceRef}
  value={resourceText}
  onChange={handleResourceTextChange}
  onPointerSelection={handleResourcePointerSelection}
  ariaLabel="Test resource JSON"
  lines={resourceHighlight}
  style={{
    minHeight: 520,
    fontSize: 11.5,
    borderBottomLeftRadius: 0,
    borderBottomRightRadius: 0,
  }}
/>
<ResourcePathStatusBar path={selectedResourcePath?.path ?? null} invalid={resourcePathInvalid} />
```

- [ ] **Step 6: Run all frontend checks**

Run:

```powershell
Set-Location frontend
npm test
npm run lint
npm run build
npm run responsive:check
```

Expected: unit tests PASS, lint exits 0, Vite build succeeds, and the Playwright
check passes at desktop, medium, small, and mobile widths.

- [ ] **Step 7: Commit the integrated status bar**

```powershell
git add frontend\scripts\responsive-check.mjs frontend\src\benches\components\HighlightedTextarea.tsx frontend\src\benches\fhirpath\FhirPathBench.tsx frontend\src\benches\fhirpath\ResourcePathStatusBar.tsx
git commit -m "Add FHIRPath resource status bar" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`nCopilot-Session: 7415203e-751e-462d-b71e-594a55a0dfd7"
```

### Task 4: Final verification

**Files:**
- Verify only; no planned file changes.

- [ ] **Step 1: Verify the complete diff**

Run:

```powershell
git --no-pager diff main...HEAD --check
git --no-pager status --short
git --no-pager log --oneline -4
```

Expected: no whitespace errors, a clean worktree, and commits for the design,
resolver, clipboard feedback, and integrated status bar.

- [ ] **Step 2: Re-run the complete frontend gate**

Run:

```powershell
Set-Location frontend
npm test
npm run lint
npm run build
npm run responsive:check
```

Expected: every command exits 0 and the responsive script reports all routes and
viewports passing.
