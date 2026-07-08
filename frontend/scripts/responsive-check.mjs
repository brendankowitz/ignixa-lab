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
    throw new Error(`${label} is not visible`);
  }
}
