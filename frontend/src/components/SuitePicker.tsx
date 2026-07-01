import { useMemo } from 'react';
import type { SuiteDescriptor } from '../types/conformance';

/** Props for {@link SuitePicker}. */
export interface SuitePickerProps {
  /** Suites available to run. */
  suites: SuiteDescriptor[];
  /** IDs of the currently selected suites. */
  selected: ReadonlySet<string>;
  /** Whether the catalog is still loading. */
  loading: boolean;
  /** Error message from loading the catalog, if any. */
  error: string | null;
  /** Invoked when a single suite is toggled. */
  onToggle: (suiteId: string) => void;
  /** Invoked to select or clear every suite at once. */
  onToggleAll: (select: boolean) => void;
}

/**
 * Presents the bundled suite catalog grouped by category with checkbox
 * selection.
 *
 * Placeholder layout — visual design is handled separately.
 */
export function SuitePicker({
  suites,
  selected,
  loading,
  error,
  onToggle,
  onToggleAll,
}: SuitePickerProps) {
  const byCategory = useMemo(() => groupSuites(suites), [suites]);

  if (loading) {
    return <p className="suite-picker__status">Loading suites…</p>;
  }

  if (error) {
    return <p className="suite-picker__status suite-picker__status--error">{error}</p>;
  }

  if (suites.length === 0) {
    return <p className="suite-picker__status">No suites available.</p>;
  }

  const allSelected = selected.size === suites.length;

  return (
    <div className="suite-picker">
      <div className="suite-picker__toolbar">
        <button type="button" onClick={() => onToggleAll(!allSelected)}>
          {allSelected ? 'Clear all' : 'Select all'}
        </button>
        <span>{selected.size} selected</span>
      </div>

      {byCategory.map(([category, categorySuites]) => (
        <fieldset key={category} className="suite-picker__category">
          <legend>{category}</legend>
          {categorySuites.map((suite) => (
            <label key={suite.id} className="suite-picker__item">
              <input
                type="checkbox"
                checked={selected.has(suite.id)}
                onChange={() => onToggle(suite.id)}
              />
              <span className="suite-picker__name">{suite.name}</span>
              <span className="suite-picker__description">{suite.description}</span>
            </label>
          ))}
        </fieldset>
      ))}
    </div>
  );
}

function groupSuites(suites: SuiteDescriptor[]): [string, SuiteDescriptor[]][] {
  const groups = new Map<string, SuiteDescriptor[]>();
  for (const suite of suites) {
    const bucket = groups.get(suite.category);
    if (bucket) {
      bucket.push(suite);
    } else {
      groups.set(suite.category, [suite]);
    }
  }
  return Array.from(groups);
}
