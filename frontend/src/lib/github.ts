/**
 * Links from a {@link ConformanceResult} back to its source TestScript fixture
 * in the `ignixa-lab` GitHub repo, shown on the Runner screen's "Test Script"
 * tab. See {@link file:../../../backend/src/Ignixa.Lab.Functions/Suites/SuiteCatalog.cs}
 * for how `ConformanceResult.file` (e.g. `"Search/intervals.json"`) is derived
 * as a path relative to this same `testscripts/` directory.
 */

/** Owner/repo this dashboard is bundled with and reads fixtures from. */
const GITHUB_REPO = 'brendankowitz/ignixa-lab';

/** Ref used to build fixture links. The running engine doesn't currently
 * expose the commit it was built from, so this isn't a true commit-pinned
 * permalink — it always resolves to the tip of `main`. */
const GITHUB_DEFAULT_REF = 'main';

/** Suite root the `file` field on a {@link ConformanceResult} is relative to. */
const TESTSCRIPTS_BASE_PATH = 'backend/src/Ignixa.Lab.Suites/testscripts';

/** Builds a `github.com/.../blob/...` URL for a bundled TestScript fixture file.
 * Only valid for results whose `file` is relative to `testscripts/` — i.e.
 * `category !== 'uploaded'`. Callers must guard on that themselves (an
 * uploaded run's `file` is just the raw uploaded filename, not a suite-relative
 * path, so this would otherwise build a plausible-looking but 404ing link). */
export function testScriptGithubUrl(file: string, ref: string = GITHUB_DEFAULT_REF): string {
  const path = `${TESTSCRIPTS_BASE_PATH}/${file}`
    .split('/')
    .map(encodeURIComponent)
    .join('/');
  return `https://github.com/${GITHUB_REPO}/blob/${ref}/${path}`;
}
