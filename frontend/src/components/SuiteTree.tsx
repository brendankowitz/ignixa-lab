import { useMemo } from 'react';
import type { SuiteRunStatus } from '../hooks/useConformanceRun';
import { groupBySuite, rollupCounts, type RolledUpCounts } from '../lib/conformance';
import type { ConformanceReport } from '../types/conformance';

/** Props for {@link SuiteTree}. */
export interface SuiteTreeProps {
  /** Suite IDs included in the current/most recent run, in run order. */
  suiteIds: string[];
  /** Resolves a suite ID to its display name. */
  suiteName: (suiteId: string) => string;
  statuses: ReadonlyMap<string, SuiteRunStatus>;
  report: ConformanceReport | null;
  completedSuiteCount: number;
  totalSuiteCount: number;
  /** Selected suite ID to filter the test list by, or null for "All suites". */
  selectedSuiteId: string | null;
  onSelect: (suiteId: string | null) => void;
}

/**
 * Left-hand suite list: one row per suite in the run, showing its live
 * status (queued / running / pass-fail counts) and acting as a filter for
 * the test list on the right.
 */
export function SuiteTree({
  suiteIds,
  suiteName,
  statuses,
  report,
  completedSuiteCount,
  totalSuiteCount,
  selectedSuiteId,
  onSelect,
}: SuiteTreeProps) {
  const countsBySuite = useMemo(() => {
    const map = new Map<string, RolledUpCounts>();
    if (report) {
      for (const group of groupBySuite(report)) {
        map.set(group.suite, rollupCounts(group.counts));
      }
    }
    return map;
  }, [report]);

  return (
    <nav className="suite-tree" aria-label="Suites">
      <span className="suite-tree__heading">Suites</span>

      <button
        type="button"
        className={`suite-tree__row${selectedSuiteId === null ? ' suite-tree__row--active' : ''}`}
        onClick={() => onSelect(null)}
      >
        <span className="suite-tree__icon suite-tree__icon--neutral">▦</span>
        <span className="suite-tree__name">All suites</span>
        <span className="suite-tree__count">
          {completedSuiteCount}/{totalSuiteCount}
        </span>
      </button>

      {suiteIds.map((suiteId) => {
        const status = statuses.get(suiteId) ?? 'queued';
        const rolled = countsBySuite.get(suiteId) ?? null;
        const running = status === 'running';
        const selected = selectedSuiteId === suiteId;
        const tone = rowTone(status, rolled);

        return (
          <button
            key={suiteId}
            type="button"
            className={`suite-tree__row${selected || running ? ' suite-tree__row--active' : ''}`}
            onClick={() => onSelect(selected ? null : suiteId)}
          >
            {running ? (
              <span className="suite-tree__spinner" aria-hidden="true" />
            ) : (
              <span className={`suite-tree__icon suite-tree__icon--${tone}`}>{rowGlyph(status, rolled)}</span>
            )}
            <span className="suite-tree__name">{suiteName(suiteId)}</span>
            <span className="suite-tree__count">{rowCountLabel(status, rolled)}</span>
          </button>
        );
      })}
    </nav>
  );
}

function rowGlyph(status: SuiteRunStatus, rolled: RolledUpCounts | null): string {
  if (status === 'queued') return '·';
  if (status === 'error' || (rolled && rolled.fail > 0)) return '✕';
  return '✓';
}

function rowTone(status: SuiteRunStatus, rolled: RolledUpCounts | null): 'neutral' | 'pass' | 'fail' {
  if (status === 'queued') return 'neutral';
  if (status === 'error' || (rolled && rolled.fail > 0)) return 'fail';
  return 'pass';
}

function rowCountLabel(status: SuiteRunStatus, rolled: RolledUpCounts | null): string {
  if (status === 'queued') return '—';
  if (status === 'error' || !rolled) return 'error';
  return `${rolled.pass}✓ ${rolled.fail}✕${rolled.skipped > 0 ? ` ${rolled.skipped}○` : ''}`;
}
