import type { CapabilitySummary } from '../types/capability';
import type { ConformanceReport } from '../types/conformance';

/**
 * Fixed interaction columns rendered by the Report screen's capability
 * coverage map — matches the backend's `CapabilityStatementParser` column set
 * exactly, so declared and observed data line up on the same axis.
 */
export const COVERAGE_COLUMNS = [
  'read',
  'vread',
  'create',
  'update',
  'patch',
  'delete',
  'search',
  'history',
] as const;

/** One column of the capability coverage grid. */
export type CoverageColumn = (typeof COVERAGE_COLUMNS)[number];

/**
 * Visual state of a single (resource, interaction) cell:
 * - `proven` — every observed call for this cell passed.
 * - `partial` — observed calls include both passes and failures.
 * - `declared-failing` — every observed call for this cell failed.
 * - `not-declared` — no observed evidence for this cell (whether or not the
 *   target's CapabilityStatement declares it — see {@link mergeCoverage}).
 */
export type CoverageCellState = 'proven' | 'partial' | 'declared-failing' | 'not-declared';

/** A single coverage-grid cell: its display state plus whether the target's CapabilityStatement declares it. */
export interface CoverageCell {
  state: CoverageCellState;
  declared: boolean;
}

/** One row of the coverage grid: a resource type and its cell per {@link COVERAGE_COLUMNS}. */
export interface CoverageRow {
  resourceType: string;
  cells: Record<CoverageColumn, CoverageCell>;
}

/** Pass/fail tally for one (resource, interaction) cell, accumulated from observed operation steps. */
interface ObservedTally {
  pass: number;
  fail: number;
}

/** Resource type -> interaction column -> observed tally. */
export type ObservedCoverage = Map<string, Map<CoverageColumn, ObservedTally>>;

/**
 * Parses a FHIR REST request into a (resourceType, interaction column) pair,
 * for the capability coverage map. Best-effort and intentionally narrow:
 * unrecognized shapes (whole-bundle transactions/batches with no resource
 * type in the path, `$operations`) return `null` and are excluded from
 * coverage entirely, rather than guessed at.
 *
 * Recognized shapes (after stripping the target's own base path):
 * ```
 *   POST   /Type                     -> create  · Type
 *   GET    /Type/{id}                -> read    · Type
 *   GET    /Type/{id}/_history/{vid} -> vread   · Type
 *   PUT    /Type/{id}                -> update  · Type
 *   PATCH  /Type/{id}                -> patch   · Type
 *   DELETE /Type/{id}                -> delete  · Type
 *   GET    /Type?...                 -> search  · Type
 *   GET    /Type/{id}/_history       -> history · Type (instance history)
 *   GET    /Type/_history            -> history · Type (type history)
 * ```
 */
export function parseOperation(
  method: string,
  url: string,
  target: string,
): { resourceType: string; column: CoverageColumn } | null {
  const segments = pathSegments(url, target);
  if (segments.length === 0 || segments.some((segment) => segment.startsWith('$'))) {
    // No resource type in the path (e.g. a transaction/batch bundle posted to
    // the base URL), or a $operation — neither maps to a single grid cell.
    return null;
  }

  const [resourceType, ...rest] = segments;
  if (!/^[A-Z][A-Za-z]*$/.test(resourceType)) {
    return null; // first segment doesn't look like a FHIR resource type
  }

  const column = interactionColumn(method.toUpperCase(), rest);
  return column ? { resourceType, column } : null;
}

/** Maps an HTTP method + the path segments following the resource type onto an interaction column. */
function interactionColumn(method: string, rest: readonly string[]): CoverageColumn | null {
  if (rest.length === 0) {
    if (method === 'POST') return 'create';
    if (method === 'GET') return 'search';
    return null;
  }
  if (rest.length === 1 && rest[0] === '_history') {
    return method === 'GET' ? 'history' : null; // /Type/_history (type-level history)
  }
  if (rest.length === 1) {
    // /Type/{id}
    if (method === 'GET') return 'read';
    if (method === 'PUT') return 'update';
    if (method === 'PATCH') return 'patch';
    if (method === 'DELETE') return 'delete';
    return null;
  }
  if (rest.length === 2 && rest[1] === '_history') {
    return method === 'GET' ? 'history' : null; // /Type/{id}/_history (instance history)
  }
  if (rest.length === 3 && rest[1] === '_history') {
    return method === 'GET' ? 'vread' : null; // /Type/{id}/_history/{vid}
  }
  return null;
}

/** Splits a request URL's path into segments, relative to the target's own base path. */
function pathSegments(url: string, target: string): string[] {
  try {
    const requestPath = new URL(url, target).pathname;
    const targetPath = new URL(target).pathname.replace(/\/+$/, '');
    const relative =
      targetPath && requestPath.startsWith(targetPath) ? requestPath.slice(targetPath.length) : requestPath;
    return relative.split('/').filter(Boolean);
  } catch {
    return [];
  }
}

/**
 * Derives observed interaction coverage from a report's operation steps: each
 * step with a captured request is parsed via {@link parseOperation} and its
 * pass/fail outcome tallied into the corresponding cell. Skipped steps carry
 * no evidence either way and are excluded.
 */
export function observedCoverage(report: ConformanceReport): ObservedCoverage {
  const coverage: ObservedCoverage = new Map();

  for (const result of report.results) {
    for (const step of result.steps) {
      if (step.kind !== 'operation' || !step.request || step.status === 'skipped') {
        continue;
      }
      const parsed = parseOperation(step.request.method, step.request.url, report.target);
      if (!parsed) {
        continue;
      }

      const byColumn = coverage.get(parsed.resourceType) ?? new Map<CoverageColumn, ObservedTally>();
      const tally = byColumn.get(parsed.column) ?? { pass: 0, fail: 0 };
      if (step.status === 'pass') {
        tally.pass += 1;
      } else {
        tally.fail += 1;
      }
      byColumn.set(parsed.column, tally);
      coverage.set(parsed.resourceType, byColumn);
    }
  }

  return coverage;
}

/** Determines a cell's visual state purely from observed evidence — declaration only affects the `declared` flag. */
function cellState(tally: ObservedTally | undefined): CoverageCellState {
  if (!tally || (tally.pass === 0 && tally.fail === 0)) {
    return 'not-declared';
  }
  if (tally.fail === 0) {
    return 'proven';
  }
  if (tally.pass === 0) {
    return 'declared-failing';
  }
  return 'partial';
}

/**
 * Merges the target's declared capabilities with observed coverage into the
 * four-state grid rows the Report screen renders. Row set is the union of
 * declared and observed resource types, sorted alphabetically. `declared` may
 * be `null` when the capability fetch failed — the grid still renders,
 * observed-only, with every cell's `declared` flag `false`.
 */
export function mergeCoverage(declared: CapabilitySummary | null, observed: ObservedCoverage): CoverageRow[] {
  const declaredByType = new Map(declared?.resources.map((resource) => [resource.type, new Set(resource.interactions)]) ?? []);
  const resourceTypes = new Set<string>([...declaredByType.keys(), ...observed.keys()]);

  return Array.from(resourceTypes)
    .sort((a, b) => a.localeCompare(b))
    .map((resourceType) => {
      const declaredColumns = declaredByType.get(resourceType);
      const observedColumns = observed.get(resourceType);

      const cells = Object.fromEntries(
        COVERAGE_COLUMNS.map((column) => [
          column,
          {
            state: cellState(observedColumns?.get(column)),
            declared: declaredColumns?.has(column) ?? false,
          } satisfies CoverageCell,
        ]),
      ) as Record<CoverageColumn, CoverageCell>;

      return { resourceType, cells };
    });
}
