import { spawn, spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';
import { stripVTControlCharacters } from 'node:util';
import { chromium } from 'playwright';

const rootUrl = 'http://127.0.0.1:4173/ignixa-lab/';
const previewReadyPattern = /Local:\s+http:\/\/127\.0\.0\.1:4173\/ignixa-lab\//;
const viewports = [
  { width: 1280, height: 900, label: 'desktop' },
  { width: 840, height: 900, label: 'medium' },
  { width: 680, height: 900, label: 'small' },
  { width: 400, height: 900, label: 'mobile' },
];

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
      await assertLandingDemoFormatting(page);
      await assertLandingDemoTransition(page);
      await assertLandingCursorBlinks(page);
      if (page.viewportSize()?.width === 1280) {
        await assertLandingAutoRotationTiming(page);
        await assertReducedMotionStopsAutoRotation(page);
      }
    },
  },
  {
    path: 'conformance.html',
    label: 'Conformance',
    checks: async (page) => {
      await mockConformanceApi(page);
      await page.reload({ waitUntil: 'networkidle' });
      await page.getByRole('button', { name: 'Setup' }).waitFor();
      await page.getByText('Foundation', { exact: true }).waitFor();
      await page.locator('#endpoint-input').fill('example.org/fhir');
      await page.getByRole('button', { name: 'Select all' }).click();
      conformanceRunRequestCount = 0;
      await page.locator('.setup-screen__start-button').click();
      await expectVisible(page.locator('.runner-status'), 'Conformance runner status');
      await expectVisible(page.locator('.suite-tree'), 'Conformance suite tree');
      await expectVisible(page.getByRole('button', { name: 'View report' }), 'Conformance view report action');
      if (conformanceRunRequestCount === 0) {
        throw new Error('Conformance run fixture was not requested.');
      }
      await expectCount(page.locator('.test-list__group-header'), 'Conformance test group headers');
      await expectCount(page.locator('.test-row'), 'Conformance test rows');
      await page.getByRole('button', { name: 'Runner', exact: true }).click();
      await assertNoHorizontalOverflow(page, 'Conformance Runner');
      await page.getByRole('button', { name: 'Report', exact: true }).click();
      await expectVisible(page.locator('.report-header'), 'Conformance report header');
      await expectVisible(page.locator('.suite-bars'), 'Conformance suite bars');
      await expectVisible(page.locator('.coverage-map'), 'Conformance coverage map');
      await assertNoHorizontalOverflow(page, 'Conformance Report');
    },
  },
  {
    path: 'lab.html',
    label: 'Benches',
    checks: async (page) => {
      await mockFakesApi(page);
      await page.reload({ waitUntil: 'networkidle' });
      await page.getByRole('tab', { name: 'FHIRPath' }).waitFor();
      await assertFhirPathResourceSelection(page);
      await assertNoHorizontalOverflow(page, 'Benches FHIRPath');
      await page.getByRole('tab', { name: 'Validation' }).click();
      await assertNoHorizontalOverflow(page, 'Benches Validation');
      await page.getByRole('tab', { name: 'Fakes' }).click();
      await expectVisible(page.getByRole('tab', { name: 'Resource' }), 'Fakes resource mode');
      await expectVisible(page.getByRole('button', { name: '⚡ Generate resource' }), 'Fakes generate resource action');
      await page.getByRole('button', { name: '⚡ Generate resource' }).click();
      await expectVisible(page.getByText('no edge cases'), 'Fakes generated resource result');
      await assertNoHorizontalOverflow(page, 'Benches Fakes');
    },
  },
];

const conformanceSuites = [
  {
    id: 'foundation-basic',
    name: 'Foundation/basic',
    description: 'Foundation smoke tests with a deliberately long suite description to exercise wrapping.',
    category: 'Foundation',
    fhirVersion: 'R4',
    file: 'Foundation/basic.json',
    testCount: 2,
    tests: [
      { name: 'metadata read', description: 'Reads the CapabilityStatement.' },
      { name: 'patient read', description: 'Reads a Patient fixture.' },
    ],
  },
  {
    id: 'search-token',
    name: 'Search/token',
    description: 'Token search tests.',
    category: 'Search',
    fhirVersion: 'R4',
    file: 'Search/token.json',
    testCount: 1,
    tests: [{ name: 'patient identifier search', description: 'Searches Patient by identifier.' }],
  },
];

const conformanceReports = {
  'foundation-basic': {
    impl: 'Responsive Fixture Server',
    target: 'https://example.org/fhir',
    fhirVersion: '4.0.1',
    startedAt: '2026-07-07T00:00:00Z',
    duration_ms: 1250,
    results: [
      conformanceResult({
        id: 'metadata-read-with-a-long-identifier-for-wrapping',
        suite: 'foundation-basic',
        file: 'Foundation/basic.json',
        category: 'Foundation',
        status: 'pass',
        method: 'GET',
        url: 'https://example.org/fhir/Patient/example',
      }),
      conformanceResult({
        id: 'patient-create-failure-with-diff',
        suite: 'foundation-basic',
        file: 'Foundation/basic.json',
        category: 'Foundation',
        status: 'fail',
        method: 'POST',
        url: 'https://example.org/fhir/Patient',
        error: {
          assertion: 'Expected HTTP 201 Created from Patient create',
          received: 'HTTP 400 Bad Request with OperationOutcome details that should wrap inside the card',
        },
      }),
    ],
  },
  'search-token': {
    impl: 'Responsive Fixture Server',
    target: 'https://example.org/fhir',
    fhirVersion: '4.0.1',
    startedAt: '2026-07-07T00:00:01Z',
    duration_ms: 850,
    results: [
      conformanceResult({
        id: 'patient-token-search',
        suite: 'search-token',
        file: 'Search/token.json',
        category: 'Search',
        status: 'skipped',
        method: 'GET',
        url: 'https://example.org/fhir/Patient?identifier=system%7Cvalue',
      }),
    ],
  },
};

const capabilitySummary = {
  target: 'https://example.org/fhir',
  fhirVersion: '4.0.1',
  resources: [
    { type: 'Patient', interactions: ['read', 'create', 'search'] },
    { type: 'Observation', interactions: ['read', 'search'] },
    { type: 'Encounter', interactions: ['read'] },
  ],
};

const fakesMetadata = {
  libraryVersion: '0.5.13',
  fhirVersions: ['r4', 'r5'],
  populationStates: ['Washington', 'Massachusetts'],
  scenarios: [],
  resourceTypesByVersion: {
    r4: ['Patient', 'Observation', 'Condition', 'Encounter', 'MedicationRequest', 'Procedure'],
    r5: ['Patient', 'Observation', 'Condition', 'Encounter', 'MedicationRequest', 'Procedure'],
  },
  observationStates: ['final', 'amended'],
  edgeCaseFamilies: [],
  patientCities: ['Seattle', 'Boston'],
  clinicalDomains: ['Cardiology', 'Oncology'],
  workflowPacks: [],
};

const generatedPatient = {
  resource: {
    resourceType: 'Patient',
    id: 'responsive-patient',
    name: [{ given: ['Responsive'], family: 'Fixture' }],
    address: [{ city: 'Seattle', state: 'WA' }],
  },
  manifest: null,
};

let conformanceRunRequestCount = 0;

const previewArgs = ['run', 'preview', '--', '--host', '127.0.0.1', '--port', '4173', '--strictPort'];
const npmCommand = process.platform === 'win32' ? (process.env.ComSpec ?? 'cmd.exe') : 'npm';
const npmArgs = process.platform === 'win32' ? ['/d', '/s', '/c', `npm.cmd ${previewArgs.join(' ')}`] : previewArgs;
const server = spawn(npmCommand, npmArgs, {
  stdio: ['ignore', 'pipe', 'pipe'],
  detached: process.platform !== 'win32',
});

let serverOutput = '';
let serverExitCode = null;
let serverExitSignal = null;
let serverStartError = null;
server.stdout.on('data', (chunk) => {
  serverOutput += chunk.toString();
});
server.stderr.on('data', (chunk) => {
  serverOutput += chunk.toString();
});
server.on('exit', (code, signal) => {
  serverExitCode = code;
  serverExitSignal = signal;
});
server.on('error', (error) => {
  serverStartError = error;
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
  stopServer();
}

function stopServer() {
  if (server.exitCode !== null || server.pid === undefined) {
    return;
  }
  if (process.platform === 'win32') {
    stopWindowsProcessTree();
    return;
  }
  try {
    process.kill(-server.pid, 'SIGTERM');
  } catch (error) {
    if (error.code !== 'ESRCH') {
      server.kill('SIGTERM');
    }
  }
}

function stopWindowsProcessTree() {
  const script = `
function Stop-Tree([int]$Id) {
  Get-CimInstance Win32_Process -Filter "ParentProcessId=$Id" | ForEach-Object { Stop-Tree $_.ProcessId }
  Stop-Process -Id $Id -Force -ErrorAction SilentlyContinue
}
Stop-Tree ${server.pid}
`;
  spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], {
    stdio: 'ignore',
  });
}

async function waitForPreview() {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    if (serverStartError !== null) {
      throw new Error(`Failed to start Vite preview.\n${serverStartError.stack ?? serverStartError.message}`);
    }
    if (server.exitCode !== null || serverExitCode !== null || serverExitSignal !== null) {
      throw new Error(`Vite preview exited early.\n${serverOutput}`);
    }
    if (hasPreviewReadyOutput()) {
      try {
        const response = await fetch(`${rootUrl}conformance.html`);
        if (response.ok) {
          return;
        }
      } catch {
        // Preview reported ready, but the port is not accepting requests yet.
      }
    }
    await delay(250);
  }
  throw new Error(`Timed out waiting for Vite preview.\n${serverOutput}`);
}

function hasPreviewReadyOutput() {
  const plainOutput = stripVTControlCharacters(serverOutput);
  return previewReadyPattern.test(plainOutput);
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
  try {
    await locator.waitFor({ state: 'visible' });
  } catch {
    const page = locator.page();
    const bodyText = await page.locator('body').innerText().catch(() => '');
    throw new Error(`${label} is not visible\n\nPage text:\n${bodyText.slice(0, 1200)}`);
  }
}

async function expectCount(locator, label, minimum = 1) {
  const count = await locator.count();
  if (count < minimum) {
    const page = locator.page();
    const bodyText = await page.locator('body').innerText().catch(() => '');
    throw new Error(`${label} count ${count} is below ${minimum}\n\nPage text:\n${bodyText.slice(0, 1200)}`);
  }
}

async function assertFhirPathResourceSelection(page) {
  const resourceTextarea = page.getByRole('textbox', { name: 'Test resource JSON' });
  await expectVisible(resourceTextarea, 'FHIRPath test resource JSON editor');
  const originalSource = await resourceTextarea.inputValue();
  const peterOffset = originalSource.indexOf('"Peter"');
  if (peterOffset < 0) {
    throw new Error('FHIRPath test resource fixture does not contain a "Peter" token.');
  }

  await resourceTextarea.evaluate((textarea, offset) => {
    textarea.focus();
    textarea.setSelectionRange(offset, offset);
    textarea.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
  }, peterOffset + 2);
  await expectVisible(page.getByText('name[0].given[0]', { exact: true }), 'FHIRPath selected resource path');
  await expectVisible(page.getByRole('button', { name: 'Copy FHIRPath' }), 'FHIRPath copy path action');
  const selectedToken = await resourceTextarea.evaluate((textarea) =>
    textarea.value.slice(textarea.selectionStart, textarea.selectionEnd),
  );
  if (selectedToken !== '"Peter"') {
    throw new Error(`FHIRPath resource selection did not select the exact token: ${selectedToken}`);
  }
  await assertNoHorizontalOverflow(page, 'Benches FHIRPath selected resource path');
  await page.evaluate(() => {
    window.__responsiveCheckClipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
  });
  try {
    await page.evaluate(() => {
      Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: { writeText: () => Promise.reject(new Error('Responsive check rejection')) },
      });
    });
    await page.getByRole('button', { name: 'Copy FHIRPath' }).click();
    await expectVisible(page.getByText('Copy failed', { exact: true }), 'FHIRPath copy failure status');
  } finally {
    await page.evaluate(() => {
      const descriptor = window.__responsiveCheckClipboardDescriptor;
      if (descriptor) {
        Object.defineProperty(navigator, 'clipboard', descriptor);
      } else {
        delete navigator.clipboard;
      }
      delete window.__responsiveCheckClipboardDescriptor;
    });
  }
  await expectVisible(page.getByText('name[0].given[0]', { exact: true }), 'FHIRPath path during copy failure');

  await resourceTextarea.fill(`${originalSource}\n`);
  await expectVisible(page.getByText('Click a JSON key or value', { exact: true }), 'FHIRPath idle resource path');

  await resourceTextarea.fill('{');
  await resourceTextarea.evaluate((textarea) => {
    textarea.focus();
    textarea.setSelectionRange(0, 0);
    textarea.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }));
  });
  await expectVisible(page.getByText('Fix JSON to inspect a path', { exact: true }), 'FHIRPath invalid resource path');

  await resourceTextarea.fill(originalSource);
}

async function assertLandingDemoFormatting(page) {
  await page.getByRole('button', { name: 'Fakes demo' }).click();
  const codeText = await page.locator('.ix-demo-code').innerText();
  if (codeText.includes('\\n')) {
    throw new Error(`Landing demo code shows escaped newline text: ${codeText}`);
  }
  if (!codeText.includes('\n')) {
    throw new Error(`Landing demo code did not render a real line break: ${codeText}`);
  }
}

async function assertLandingDemoTransition(page) {
  await page.getByRole('button', { name: 'Validation demo' }).click();
  const hasTransitionClass = await page
    .locator('.ix-hero-demo')
    .evaluate((element) => element.classList.contains('ix-hero-demo--transitioning'));
  if (!hasTransitionClass) {
    throw new Error('Landing demo did not apply transition class after manual selection.');
  }
}

async function assertLandingCursorBlinks(page) {
  const cursorAnimation = await page.locator('.ix-demo-cursor').evaluate((element) => {
    const style = window.getComputedStyle(element);
    return {
      name: style.animationName,
      duration: style.animationDuration,
      iterationCount: style.animationIterationCount,
      timingFunction: style.animationTimingFunction,
    };
  });
  if (
    cursorAnimation.name !== 'ixblink' ||
    cursorAnimation.duration !== '1.1s' ||
    cursorAnimation.iterationCount !== 'infinite' ||
    !cursorAnimation.timingFunction.startsWith('steps(1')
  ) {
    throw new Error(`Landing demo cursor blink animation is incorrect: ${JSON.stringify(cursorAnimation)}`);
  }
}

async function assertLandingAutoRotationTiming(page) {
  await page.reload({ waitUntil: 'networkidle' });
  const demo = page.locator('.ix-hero-demo');
  await expectVisible(demo, 'Landing auto-rotation demo');
  const initialDemo = await demo.getAttribute('data-active-demo');
  if (!initialDemo) {
    throw new Error('Landing auto-rotation demo is missing data-active-demo');
  }
  await page.waitForTimeout(4500);
  const earlyDemo = await demo.getAttribute('data-active-demo');
  if (earlyDemo !== initialDemo) {
    throw new Error(`Landing demo rotated before 10s: ${initialDemo} -> ${earlyDemo}`);
  }
  await page.waitForFunction(
    (initial) => document.querySelector('.ix-hero-demo')?.getAttribute('data-active-demo') !== initial,
    initialDemo,
    { timeout: 7000 },
  );
}

async function assertReducedMotionStopsAutoRotation(page) {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  try {
    await page.reload({ waitUntil: 'networkidle' });
    const demo = page.locator('.ix-hero-demo');
    await expectVisible(demo, 'Landing reduced-motion demo');
    await assertLandingCursorBlinks(page);
    const initialDemo = await demo.getAttribute('data-active-demo');
    if (!initialDemo) {
      throw new Error('Landing reduced-motion demo is missing data-active-demo');
    }
    await page.waitForTimeout(10_500);
    const laterDemo = await demo.getAttribute('data-active-demo');
    if (initialDemo !== laterDemo) {
      throw new Error(`Landing demo rotated despite reduced motion: ${initialDemo} -> ${laterDemo}`);
    }
  } finally {
    await page.emulateMedia({ reducedMotion: 'no-preference' });
  }
}

async function mockConformanceApi(page) {
  await page.route('**/api/health', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ status: 'ok', engineVersion: 'responsive-check', testScriptsRevision: 'main' }),
    }),
  );
  await page.route('**/api/suites', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify(conformanceSuites),
    }),
  );
  await page.route('**/api/run', async (route) => {
    conformanceRunRequestCount += 1;
    const request = JSON.parse(route.request().postData() ?? '{}');
    const suiteId = request.suiteIds?.[0];
    const report = conformanceReports[suiteId];
    if (!report) {
      await route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({ error: `No responsive fixture report for suite ${suiteId}` }),
      });
      return;
    }
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify(report),
    });
  });
  await page.route('**/api/capability**', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify(capabilitySummary),
    }),
  );
}

async function mockFakesApi(page) {
  await page.route('**/api/fakes/metadata', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify(fakesMetadata),
    }),
  );
  await page.route('**/api/fakes/resource', (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify(generatedPatient),
    }),
  );
}

function conformanceResult({ id, suite, file, category, status, method, url, error = null }) {
  return {
    id,
    file,
    suite,
    category,
    status,
    duration_ms: 320,
    error,
    steps: [
      {
        phase: 'test',
        kind: 'operation',
        status,
        duration_ms: 120,
        label: `${method} ${url}`,
        description: 'HTTP operation captured for responsive coverage rendering.',
        message: status === 'fail' ? 'Operation returned an unexpected response.' : null,
        request: {
          method,
          url,
          headers: { accept: 'application/fhir+json' },
          body: method === 'POST' ? '{"resourceType":"Patient"}' : null,
        },
        response: {
          statusCode: status === 'fail' ? 400 : 200,
          headers: { 'content-type': 'application/fhir+json' },
          body: status === 'fail'
            ? '{"resourceType":"OperationOutcome","issue":[{"severity":"error","diagnostics":"Long diagnostic text for wrapping."}]}'
            : '{"resourceType":"Patient","id":"example"}',
          bodyParseError: null,
        },
      },
      {
        phase: 'test',
        kind: 'assertion',
        status,
        duration_ms: 12,
        label: 'response status assertion with a long label to exercise row wrapping',
        description: 'Verifies the response status.',
        message: status === 'fail' ? 'Expected 201 but received 400.' : 'Status matched expectation.',
        request: null,
        response: null,
      },
    ],
  };
}
