/**
 * Maps a {@link ConformanceReport} onto a FHIR `Bundle` of `TestReport`
 * resources — one `TestReport` per (suite, file) group, since the FHIR
 * `TestReport` resource is defined as the result of executing a single
 * `TestScript`, while one conformance run here covers many suite files at
 * once. Setup and teardown steps are identical across every test case in a
 * file (`ConformanceReportMapper` on the backend folds the same setup/
 * teardown result into each test case's step list), so they're read from the
 * first result in the group rather than repeated per test.
 */
import { aggregateStatus, countByStatus, groupByFile, passRate } from './conformance';
import { testScriptGithubUrl } from './github';
import type { ConformanceReport, ConformanceStatus, ConformanceStep } from '../types/conformance';

/** `http://hl7.org/fhir/ValueSet/report-action-result-codes` */
type TestReportActionResult = 'pass' | 'skip' | 'fail' | 'warning' | 'error' | 'not-tested';

interface TestReportActionDetail {
  result: TestReportActionResult;
  message?: string;
}

/** Exactly one of `operation`/`assert` — FHIR requires a setup/test/teardown action to contain one or the other, never both or neither. */
type TestReportAction =
  | { operation: TestReportActionDetail; assert?: never }
  | { operation?: never; assert: TestReportActionDetail };

interface TestReportTest {
  name: string;
  action: TestReportAction[];
}

/** `uri` is 1..1 (required) per the FHIR R4 TestReport.participant definition, not optional. */
interface TestReportParticipant {
  type: 'test-engine' | 'client' | 'server';
  uri: string;
  display?: string;
}

interface TestReportResource {
  resourceType: 'TestReport';
  status: 'completed';
  testScript: { reference?: string; display: string };
  result: 'pass' | 'fail' | 'pending';
  score: number;
  tester: string;
  issued: string;
  participant: TestReportParticipant[];
  setup?: { action: TestReportAction[] };
  test: TestReportTest[];
  teardown?: { action: TestReportAction[] };
}

interface TestReportBundle {
  resourceType: 'Bundle';
  type: 'collection';
  timestamp: string;
  entry: Array<{ fullUrl: string; resource: TestReportResource }>;
}

const ACTION_RESULT_BY_STATUS: Record<ConformanceStatus, TestReportActionResult> = {
  pass: 'pass',
  fail: 'fail',
  error: 'error',
  skipped: 'skip',
};

function slugify(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-+|-+$)/g, '');
}

/** Strips the `"<TestScriptName> > "` prefix `ConformanceReportMapper` adds to a test case's id, leaving just its own name. */
function testCaseName(resultId: string): string {
  const separator = resultId.indexOf(' > ');
  return separator === -1 ? resultId : resultId.slice(separator + 3);
}

function mapStep(step: ConformanceStep): TestReportAction {
  const detail: TestReportActionDetail = {
    result: ACTION_RESULT_BY_STATUS[step.status],
    message: step.message ?? step.description ?? undefined,
  };
  return step.kind === 'operation' ? { operation: detail } : { assert: detail };
}

/** Builds one `Bundle` of `TestReport` resources — one per (suite, file) group — from a completed conformance run. */
export function buildTestReportBundle(report: ConformanceReport, testScriptsRevision: string | null): TestReportBundle {
  const groups = groupByFile(report.results);

  const entries = groups.map((group) => {
    const first = group.results[0];
    const setupActions = first.steps.filter((step) => step.phase === 'setup').map(mapStep);
    const teardownActions = first.steps.filter((step) => step.phase === 'teardown').map(mapStep);

    const tests: TestReportTest[] = group.results.map((result) => ({
      name: testCaseName(result.id),
      action: result.steps.filter((step) => step.phase === 'test').map(mapStep),
    }));

    const overall = aggregateStatus(group.results);
    const result: TestReportResource['result'] =
      overall === 'pass' ? 'pass' : overall === 'skipped' ? 'pending' : 'fail';

    const isUploaded = first.category === 'uploaded';
    const id = slugify(`${group.suite}-${group.file}`);

    const resource: TestReportResource = {
      resourceType: 'TestReport',
      status: 'completed',
      testScript: isUploaded
        ? { display: group.file }
        : { reference: testScriptGithubUrl(group.file, testScriptsRevision ?? undefined), display: group.file },
      result,
      score: Math.round(passRate(countByStatus(group.results)) * 100),
      tester: report.impl,
      issued: report.startedAt,
      participant: [
        { type: 'server', uri: report.target },
        { type: 'test-engine', uri: `urn:ignixa-lab:test-engine:${encodeURIComponent(report.impl)}`, display: report.impl },
      ],
      ...(setupActions.length > 0 ? { setup: { action: setupActions } } : {}),
      test: tests,
      ...(teardownActions.length > 0 ? { teardown: { action: teardownActions } } : {}),
    };

    return { fullUrl: `TestReport/${id}`, resource };
  });

  return {
    resourceType: 'Bundle',
    type: 'collection',
    timestamp: report.startedAt,
    entry: entries,
  };
}

/** Builds and downloads the {@link buildTestReportBundle} result as a `.json` file. */
export function downloadTestReportBundle(report: ConformanceReport, testScriptsRevision: string | null): void {
  const bundle = buildTestReportBundle(report, testScriptsRevision);
  const blob = new Blob([JSON.stringify(bundle, null, 2)], { type: 'application/fhir+json' });
  const url = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `conformance-testreport-${report.startedAt.replace(/[^0-9a-z]+/gi, '-')}.json`;
    anchor.click();
  } finally {
    // Deferred rather than revoked immediately: some browsers process the download
    // asynchronously, and revoking the blob URL before that completes can silently
    // cancel it with no error visible to this code or the user.
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}
