import type { RunPhase } from '../hooks/useConformanceRun';

/** Props for {@link ProgressPanel}. */
export interface ProgressPanelProps {
  /** Current run lifecycle phase. */
  phase: RunPhase;
  /** Number of suites included in the current run. */
  suiteCount: number;
  /** Error message when the run failed. */
  error: string | null;
}

/**
 * Shows run progress and surfaces run-level errors.
 *
 * NOTE: `POST /api/run` resolves once (no incremental progress yet), so this is
 * an indeterminate indicator while `phase === 'running'`. It becomes a
 * determinate progress bar once the backend streams per-suite updates.
 *
 * Placeholder layout — visual design is handled separately.
 */
export function ProgressPanel({ phase, suiteCount, error }: ProgressPanelProps) {
  if (phase === 'idle') {
    return null;
  }

  if (phase === 'error') {
    return (
      <div className="progress-panel progress-panel--error" role="alert">
        <p>{error ?? 'The run failed.'}</p>
      </div>
    );
  }

  if (phase === 'running') {
    return (
      <div className="progress-panel progress-panel--running" role="status">
        <p>Running {suiteCount} suite{suiteCount === 1 ? '' : 's'}…</p>
        <div className="progress-panel__bar progress-panel__bar--indeterminate" />
      </div>
    );
  }

  return null;
}
