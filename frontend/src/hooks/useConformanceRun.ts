import { useCallback, useEffect, useState } from 'react';
import { ApiError, getSuites, runConformance } from '../api/client';
import type {
  ConformanceReport,
  RunRequest,
  SuiteDescriptor,
} from '../types/conformance';

/** Lifecycle phase of a conformance run. */
export type RunPhase = 'idle' | 'running' | 'complete' | 'error';

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
  /** The most recent report, or null before the first successful run. */
  report: ConformanceReport | null;
  /** Error message from the most recent run, if it failed. */
  runError: string | null;
  /** Starts a run with the given request body. */
  start: (request: RunRequest) => Promise<void>;
  /** Cancels an in-flight run. */
  cancel: () => void;
  /** Clears the current report and error, returning to the idle state. */
  reset: () => void;
}

/**
 * Owns the state for loading the suite catalog and executing a conformance run.
 *
 * NOTE: `POST /api/run` currently returns a single report once the whole run
 * completes, so `phase` moves `idle -> running -> complete`. When the backend
 * grows a streaming/progress channel, per-suite progress can be surfaced here
 * without changing the component API.
 */
export function useConformanceRun(): ConformanceRunState {
  const [suites, setSuites] = useState<SuiteDescriptor[]>([]);
  const [suitesLoading, setSuitesLoading] = useState(true);
  const [suitesError, setSuitesError] = useState<string | null>(null);

  const [phase, setPhase] = useState<RunPhase>('idle');
  const [report, setReport] = useState<ConformanceReport | null>(null);
  const [runError, setRunError] = useState<string | null>(null);
  const [controller, setController] = useState<AbortController | null>(null);

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

  const start = useCallback(async (request: RunRequest) => {
    const abort = new AbortController();
    setController(abort);
    setPhase('running');
    setRunError(null);

    try {
      const result = await runConformance(request, abort.signal);
      setReport(result);
      setPhase('complete');
    } catch (error: unknown) {
      if (abort.signal.aborted) {
        setPhase('idle');
        return;
      }
      setRunError(describeError(error));
      setPhase('error');
    } finally {
      setController(null);
    }
  }, []);

  const cancel = useCallback(() => {
    controller?.abort();
  }, [controller]);

  const reset = useCallback(() => {
    controller?.abort();
    setReport(null);
    setRunError(null);
    setPhase('idle');
  }, [controller]);

  return {
    suites,
    suitesLoading,
    suitesError,
    phase,
    report,
    runError,
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
