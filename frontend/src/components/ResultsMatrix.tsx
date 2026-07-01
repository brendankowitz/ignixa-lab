import { useMemo } from 'react';
import { groupByCategory } from '../lib/conformance';
import type { ConformanceReport } from '../types/conformance';
import { CategoryGroup } from './CategoryGroup';

/** Props for {@link ResultsMatrix}. */
export interface ResultsMatrixProps {
  /** The completed conformance report to display. */
  report: ConformanceReport;
}

/**
 * Category- and scenario-based results view: one collapsible
 * {@link CategoryGroup} per category, each listing its test-case rows.
 *
 * Placeholder layout — visual design is handled separately.
 */
export function ResultsMatrix({ report }: ResultsMatrixProps) {
  const groups = useMemo(() => groupByCategory(report), [report]);

  if (groups.length === 0) {
    return <p className="results-matrix__empty">No results to display.</p>;
  }

  return (
    <div className="results-matrix">
      {groups.map((group) => (
        <CategoryGroup key={group.category} group={group} />
      ))}
    </div>
  );
}
