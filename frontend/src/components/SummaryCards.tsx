import type { StatusCounts } from '../lib/conformance';
import { passRate, STATUS_LABELS } from '../lib/conformance';
import type { ConformanceStatus } from '../types/conformance';

/** Props for {@link SummaryCards}. */
export interface SummaryCardsProps {
  /** Result totals by status. */
  counts: StatusCounts;
  /** Total wall-clock duration of the run, in milliseconds. */
  durationMs: number;
}

const STATUSES: readonly ConformanceStatus[] = ['pass', 'fail', 'error', 'skipped'];

/**
 * High-level scorecards: pass rate plus per-status totals and run duration.
 *
 * Placeholder layout — visual design (including any radial charts) is handled
 * separately.
 */
export function SummaryCards({ counts, durationMs }: SummaryCardsProps) {
  const total = counts.pass + counts.fail + counts.error + counts.skipped;
  const rate = Math.round(passRate(counts) * 100);

  return (
    <div className="summary-cards">
      <div className="summary-cards__card summary-cards__card--rate">
        <span className="summary-cards__value">{rate}%</span>
        <span className="summary-cards__label">Pass rate</span>
      </div>

      {STATUSES.map((status) => (
        <div
          key={status}
          className={`summary-cards__card summary-cards__card--${status}`}
        >
          <span className="summary-cards__value">{counts[status]}</span>
          <span className="summary-cards__label">{STATUS_LABELS[status]}</span>
        </div>
      ))}

      <div className="summary-cards__card summary-cards__card--meta">
        <span className="summary-cards__value">{total}</span>
        <span className="summary-cards__label">Total tests</span>
      </div>

      <div className="summary-cards__card summary-cards__card--meta">
        <span className="summary-cards__value">{formatDuration(durationMs)}</span>
        <span className="summary-cards__label">Duration</span>
      </div>
    </div>
  );
}

function formatDuration(durationMs: number): string {
  if (durationMs < 1000) {
    return `${durationMs} ms`;
  }
  return `${(durationMs / 1000).toFixed(1)} s`;
}
