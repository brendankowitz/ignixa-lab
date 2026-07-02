import { formatDuration, type RolledUpCounts } from '../lib/conformance';
import type { RunPhase } from '../hooks/useConformanceRun';

/** Props for {@link RunnerStatusBar}. */
export interface RunnerStatusBarProps {
  phase: RunPhase;
  /** Display name of the suite currently in flight, if any. */
  currentSuiteName: string | null;
  completedSuiteCount: number;
  totalSuiteCount: number;
  tallies: RolledUpCounts;
  durationMs: number;
  runError: string | null;
  onViewReport: () => void;
}

/**
 * The Runner screen's status strip, in its two non-idle states: `running`
 * (spinner, progress, live tallies) and `complete`/`error` (a summary chip,
 * final tallies + duration, and a "View report →" link). The `idle` state —
 * before any suite has ever run — has nothing to show here; the Runner
 * screen renders a plain prompt instead of mounting this component.
 */
export function RunnerStatusBar({
  phase,
  currentSuiteName,
  completedSuiteCount,
  totalSuiteCount,
  tallies,
  durationMs,
  runError,
  onViewReport,
}: RunnerStatusBarProps) {
  if (phase === 'running') {
    const progressPct = totalSuiteCount === 0 ? 0 : Math.round((completedSuiteCount / totalSuiteCount) * 100);
    return (
      <div className="runner-status runner-status--running" role="status">
        <span className="runner-status__spinner" aria-hidden="true" />
        <span className="runner-status__label">
          Running <strong>{currentSuiteName ?? 'warming up…'}</strong>
        </span>
        <div className="runner-status__bar">
          <div className="runner-status__bar-fill" style={{ width: `${progressPct}%` }} />
        </div>
        <span className="runner-status__count">
          {completedSuiteCount} / {totalSuiteCount}
        </span>
        <TallyReadout tallies={tallies} />
      </div>
    );
  }

  const failed = phase === 'error';
  return (
    <div className={`runner-status runner-status--done${failed ? ' runner-status--failed' : ''}`} role="status">
      <span className={`runner-status__chip${failed ? ' runner-status__chip--failed' : ''}`}>
        {failed ? 'RUN FAILED' : 'RUN COMPLETE'}
      </span>
      <span className="runner-status__summary">
        <span className="runner-status__summary-pass">{tallies.pass} passed</span> ·{' '}
        <span className="runner-status__summary-fail">{tallies.fail} failed</span> · {tallies.skipped} skipped ·{' '}
        {formatDuration(durationMs)}
      </span>
      {failed && runError ? <span className="runner-status__error">{runError}</span> : null}
      <div className="runner-status__spacer" />
      <button type="button" className="runner-status__view-report" onClick={onViewReport}>
        View report →
      </button>
    </div>
  );
}

function TallyReadout({ tallies }: { tallies: RolledUpCounts }) {
  return (
    <span className="runner-status__tallies">
      <span className="runner-status__tally runner-status__tally--pass">{tallies.pass} ✓</span>{' '}
      <span className="runner-status__tally runner-status__tally--fail">{tallies.fail} ✕</span>{' '}
      <span className="runner-status__tally runner-status__tally--skip">{tallies.skipped} ○</span>
    </span>
  );
}
