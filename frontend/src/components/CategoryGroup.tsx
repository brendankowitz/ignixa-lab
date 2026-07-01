import { useState } from 'react';
import type { CategoryGroup as CategoryGroupData } from '../lib/conformance';
import { STATUS_LABELS } from '../lib/conformance';
import type { ConformanceStatus } from '../types/conformance';
import { ResultRow } from './ResultRow';

/** Props for {@link CategoryGroup}. */
export interface CategoryGroupProps {
  /** The category and its aggregated results. */
  group: CategoryGroupData;
}

const STATUSES: readonly ConformanceStatus[] = ['pass', 'fail', 'error', 'skipped'];

/**
 * A collapsible category section: header shows the rolled-up status and
 * per-status tallies; the body lists each {@link ResultRow}.
 *
 * Placeholder layout — visual design is handled separately.
 */
export function CategoryGroup({ group }: CategoryGroupProps) {
  const [collapsed, setCollapsed] = useState(false);

  return (
    <section className={`category-group category-group--${group.status}`}>
      <button
        type="button"
        className="category-group__header"
        aria-expanded={!collapsed}
        onClick={() => setCollapsed((value) => !value)}
      >
        <span className="category-group__name">{group.category}</span>
        <span className="category-group__counts">
          {STATUSES.map((status) =>
            group.counts[status] > 0 ? (
              <span
                key={status}
                className={`category-group__count category-group__count--${status}`}
                title={STATUS_LABELS[status]}
              >
                {group.counts[status]}
              </span>
            ) : null,
          )}
        </span>
      </button>

      {collapsed ? null : (
        <div className="category-group__body">
          {group.results.map((result) => (
            <ResultRow key={result.id} result={result} />
          ))}
        </div>
      )}
    </section>
  );
}
