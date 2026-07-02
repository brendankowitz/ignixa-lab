import { Fragment } from 'react';
import { COVERAGE_COLUMNS, type CoverageCell, type CoverageRow } from '../lib/coverage';

/** Props for {@link CoverageMap}. */
export interface CoverageMapProps {
  rows: CoverageRow[];
}

/**
 * Resource-type x interaction grid: each cell is colored by whether this
 * conformance run has *proven* the interaction (all observed calls passed),
 * seen mixed results, seen it consistently fail, or has no evidence either
 * way. See `lib/coverage.ts#mergeCoverage` for how the four states are derived.
 */
export function CoverageMap({ rows }: CoverageMapProps) {
  if (rows.length === 0) {
    return <p className="coverage-map__empty">No resource types observed or declared yet.</p>;
  }

  return (
    <>
      <div className="coverage-map">
        <span className="coverage-map__corner" />
        {COVERAGE_COLUMNS.map((column) => (
          <span key={column} className="coverage-map__column-label">
            {column}
          </span>
        ))}

        {rows.map((row) => (
          <Fragment key={row.resourceType}>
            <span className="coverage-map__row-label">{row.resourceType}</span>
            {COVERAGE_COLUMNS.map((column) => {
              const cell = row.cells[column];
              return (
                <div
                  key={column}
                  title={`${row.resourceType} · ${column} — ${tooltipText(cell)}`}
                  className={`coverage-map__cell coverage-map__cell--${cell.state}`}
                />
              );
            })}
          </Fragment>
        ))}
      </div>

      <div className="coverage-map__legend">
        <LegendItem stateClass="proven" label="proven (all pass)" />
        <LegendItem stateClass="partial" label="partial pass" />
        <LegendItem stateClass="declared-failing" label="declared, failing" />
        <LegendItem stateClass="not-declared" label="not declared" />
      </div>
    </>
  );
}

function LegendItem({ stateClass, label }: { stateClass: string; label: string }) {
  return (
    <span className="coverage-map__legend-item">
      <span className={`coverage-map__legend-swatch coverage-map__legend-swatch--${stateClass}`} />
      {label}
    </span>
  );
}

function tooltipText(cell: CoverageCell): string {
  switch (cell.state) {
    case 'proven':
      return 'proven — all observed calls passed';
    case 'partial':
      return 'partial — mixed pass/fail';
    case 'declared-failing':
      return 'declared, failing — all observed calls failed';
    case 'not-declared':
      return cell.declared ? 'declared, not yet exercised' : 'not declared';
  }
}
