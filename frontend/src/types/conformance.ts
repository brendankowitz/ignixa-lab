/**
 * TypeScript mirror of the backend conformance report schema.
 *
 * The JSON shapes here match the C# records under
 * `backend/src/Ignixa.Lab.Functions/Conformance` and `.../Models`, including the
 * `duration_ms` snake_case fields, so a {@link ConformanceReport} deserialized
 * from `POST /api/run` (or a `conformance/latest.json` dashboard artifact) maps
 * directly onto these interfaces.
 */

/** Outcome of a single test case or step. */
export type ConformanceStatus = 'pass' | 'fail' | 'error' | 'skipped';

/** Phase a step belongs to within a test case. */
export type ConformancePhase = 'setup' | 'test' | 'teardown';

/** Whether a step exercised an operation or evaluated an assertion. */
export type ConformanceStepKind = 'operation' | 'assertion';

/** Captured HTTP request for a step trace (best-effort). */
export interface ConformanceHttpRequest {
  method: string;
  url: string;
  headers: Record<string, string>;
  body: string | null;
}

/** Captured HTTP response for a step trace (best-effort). */
export interface ConformanceHttpResponse {
  statusCode: number;
  headers: Record<string, string>;
  body: string | null;
  bodyParseError: string | null;
}

/** Summary of the first failing assertion for a test case. */
export interface ConformanceError {
  assertion: string | null;
  received: string | null;
}

/** One alternative's outcome within a grouped (assertionAnyOfGroup) step. */
export interface ConformanceGroupMember {
  description: string | null;
  applicable: boolean;
  passed: boolean;
  message: string | null;
}

/** A single phase/action within a test case (setup, test, or teardown step). */
export interface ConformanceStep {
  phase: ConformancePhase;
  kind: ConformanceStepKind;
  status: ConformanceStatus;
  duration_ms: number;
  label: string | null;
  description: string | null;
  message: string | null;
  request: ConformanceHttpRequest | null;
  response: ConformanceHttpResponse | null;
  group_id: string | null;
  members: ConformanceGroupMember[] | null;
}

/** Result of executing a single TestScript (test case) within a suite. */
export interface ConformanceResult {
  id: string;
  file: string;
  suite: string;
  category: string;
  status: ConformanceStatus;
  duration_ms: number;
  error: ConformanceError | null;
  steps: ConformanceStep[];
}

/** Top-level conformance report produced by a run against a target server. */
export interface ConformanceReport {
  impl: string;
  target: string;
  fhirVersion: string;
  startedAt: string;
  duration_ms: number;
  results: ConformanceResult[];
}

/** A single test case defined within a suite. */
export interface SuiteTest {
  name: string;
  description: string | null;
}

/** Metadata describing a bundled TestScript suite (`GET /api/suites`). */
export interface SuiteDescriptor {
  id: string;
  name: string;
  description: string;
  category: string;
  fhirVersion: string;
  file: string;
  /** Number of test cases defined in the suite (before any runtime parametrization). */
  testCount: number;
  /** The individual test cases defined in the suite, listed when the row is expanded. */
  tests: SuiteTest[];
}

/** Response from the `GET /api/health` liveness endpoint. */
export interface HealthResponse {
  status: string;
  engineVersion: string;
  /** Commit the bundled testscripts fixtures were packed from, or `null` if it couldn't be
   * determined; see `lib/github.ts#testScriptGithubUrl`. */
  testScriptsRevision: string | null;
}

/** An inline TestScript supplied in a run request. */
export interface UploadedTestScript {
  fileName: string;
  content: string;
}

/** Request body for `POST /api/run`. */
export interface RunRequest {
  targetUrl: string;
  suiteIds?: string[];
  fhirVersion?: string;
  uploadedTestScripts?: UploadedTestScript[];
}
