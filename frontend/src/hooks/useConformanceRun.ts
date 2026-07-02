import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, getSuites, runConformance } from '../api/client';
import { mergeReports } from '../lib/conformance';
import type { ConformanceReport, RunRequest, SuiteDescriptor } from '../types/conformance';

/** Lifecycle phase of a conformance run. */
export type RunPhase = 'idle' | 'running' | 'complete' | 'error';

/** Per-suite progress within a run, tracked as each `/api/run` call lands. */
export type SuiteRunStatus = 'queued' | 'running' | 'complete' | 'error';

/** Request to start a run: target server, FHIR version, and the suites to execute. */
export interface StartRunRequest {
  targetUrl: string;
  fhirVersion: string;
  suiteIds: string[];
}

/** Public shape returned by {@link useConformanceRun}. */
export interface ConformanceRunState {
  /** Bundled suites available to run, loaded from `GET /api/suites`. */
  suites: SuiteDescriptor[];
  /** True while the suite catalog is loading. */
  suitesLoading: boolean;
  /** Error message from loading the suite catalog, if any. */
  suitesError: string | null;
  /** Current run lifecycle phase. */
  phase: RunPhase;
  /** The report merged so far across completed suites, or null before the first result lands. */
  report: ConformanceReport | null;
  /** Error message from the most recent failed suite, if any (the run may still continue). */
  runError: string | null;
  /** Per-suite status for the suite IDs included in the current/most recent run. */
  suiteStatuses: ReadonlyMap<string, SuiteRunStatus>;
  /** Suite ID currently in flight, or null when idle/between suites. */
  currentSuiteId: string | null;
  /** Total number of suites in the current/most recent run. */
  totalSuiteCount: number;
  /** Number of suites that have finished (complete or error). */
  completedSuiteCount: number;
  /** Starts a run: calls `POST /api/run` once per suite, sequentially. */
  start: (request: StartRunRequest) => Promise<void>;
  /** Aborts the in-flight suite and cancels the remaining queue. */
  cancel: () => void;
  /** Clears the current report and returns to the idle state. */
  reset: () => void;
}

/**
 * Owns the suite catalog and drives a conformance run.
 *
 * `POST /api/run` returns a single report once a suite completes and has no
 * streaming channel, so `start` issues **one call per selected suite**,
 * sequentially, under a shared `AbortController`, merging each result as it
 * arrives (see {@link mergeReports}). This gives real, coarse-grained (per
 * suite, not per test) live progress with no backend change: the suite tree
 * and status bar can show queued -> running -> complete/error per suite as
 * the run proceeds.
 *
 * A single suite failing (a real error, not a user cancel) does not abort the
 * rest of the queue — it's recorded via `suiteStatuses` and `runError`, and
 * the run continues so the remaining suites still get a chance to execute.
 */
export function useConformanceRun(): ConformanceRunState {
  const [suites, setSuites] = useState<SuiteDescriptor[]>([]);
  const [suitesLoading, setSuitesLoading] = useState(true);
  const [suitesError, setSuitesError] = useState<string | null>(null);

  const [phase, setPhase] = useState<RunPhase>('idle');
  const [report, setReport] = useState<ConformanceReport | null>(null);
  const [runError, setRunError] = useState<string | null>(null);
  const [suiteStatuses, setSuiteStatuses] = useState<Map<string, SuiteRunStatus>>(new Map());
  const [currentSuiteId, setCurrentSuiteId] = useState<string | null>(null);
  const [totalSuiteCount, setTotalSuiteCount] = useState(0);

  // Mutable ref rather than state: the abort controller is an imperative
  // handle for `cancel`, not something that should trigger a re-render itself.
  const controllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const abort = new AbortController();
    getSuites(abort.signal)
      .then((loaded) => {
        setSuites(loaded);
        setSuitesError(null);
      })
      .catch((error: unknown) => {
        if (!abort.signal.aborted) {
          setSuitesError(describeError(error));
        }
      })
      .finally(() => {
        if (!abort.signal.aborted) {
          setSuitesLoading(false);
        }
      });
    return () => abort.abort();
  }, []);

  const start = useCallback(async ({ targetUrl, fhirVersion, suiteIds }: StartRunRequest) => {
    const abort = new AbortController();
    controllerRef.current = abort;

    setPhase('running');
    setRunError(null);
    setReport(null);
    setSuiteStatuses(new Map(suiteIds.map((id) => [id, 'queued' as SuiteRunStatus])));
    setTotalSuiteCount(suiteIds.length);
    setCurrentSuiteId(null);

    let merged: ConformanceReport | null = null;
    let sawFailure = false;

    for (const suiteId of suiteIds) {
      if (abort.signal.aborted) {
        break;
      }

      setCurrentSuiteId(suiteId);
      setSuiteStatuses((previous) => new Map(previous).set(suiteId, 'running'));

      const request: RunRequest = { targetUrl, fhirVersion, suiteIds: [suiteId] };
      try {
        const suiteReport = await runConformance(request, abort.signal);
        merged = mergeReports(merged, suiteReport);
        setReport(merged);
        setSuiteStatuses((previous) => new Map(previous).set(suiteId, 'complete'));
      } catch (error) {
        // A user-initiated stop surfaces here as an AbortError; treat the
        // interrupted suite as not completed and leave the rest of the queue
        // untouched (still 'queued') rather than marking every remaining
        // suite as failed.
        setSuiteStatuses((previous) => new Map(previous).set(suiteId, 'error'));
        if (abort.signal.aborted) {
          break;
        }
        sawFailure = true;
        setRunError(describeError(error));
      }
    }

    setCurrentSuiteId(null);
    controllerRef.current = null;
    // A run "completes" once at least one suite produced a report, even if it
    // was stopped early or another suite failed along the way — there's real
    // data worth viewing. Only report 'error' when nothing came back at all.
    setPhase(merged ? 'complete' : sawFailure ? 'error' : 'idle');
  }, []);

  const cancel = useCallback(() => {
    controllerRef.current?.abort();
  }, []);

  const reset = useCallback(() => {
    controllerRef.current?.abort();
    setReport(null);
    setRunError(null);
    setSuiteStatuses(new Map());
    setCurrentSuiteId(null);
    setTotalSuiteCount(0);
    setPhase('idle');
  }, []);

  const completedSuiteCount = Array.from(suiteStatuses.values()).filter(
    (status) => status === 'complete' || status === 'error',
  ).length;

  return {
    suites,
    suitesLoading,
    suitesError,
    phase,
    report,
    runError,
    suiteStatuses,
    currentSuiteId,
    totalSuiteCount,
    completedSuiteCount,
    start,
    cancel,
    reset,
  };
}

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message;
  }
  if (error instanceof Error) {
    return error.message;
  }
  return 'An unexpected error occurred.';
}
