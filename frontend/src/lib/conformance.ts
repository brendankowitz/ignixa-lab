import type {
  ConformanceReport,
  ConformanceResult,
  ConformanceStatus,
} from '../types/conformance';

/** Ordered list of statuses, from most to least severe. */
export const STATUS_ORDER: readonly ConformanceStatus[] = [
  'error',
  'fail',
  'skipped',
  'pass',
];

/** Human-readable label for a status. */
export const STATUS_LABELS: Record<ConformanceStatus, string> = {
  pass: 'Passed',
  fail: 'Failed',
  error: 'Errored',
  skipped: 'Skipped',
};

/** Count of results per status. */
export type StatusCounts = Record<ConformanceStatus, number>;

/** Returns a zeroed {@link StatusCounts} record. */
export function emptyStatusCounts(): StatusCounts {
  return { pass: 0, fail: 0, error: 0, skipped: 0 };
}

/** Tallies results by status. */
export function countByStatus(results: readonly ConformanceResult[]): StatusCounts {
  const counts = emptyStatusCounts();
  for (const result of results) {
    counts[result.status] += 1;
  }
  return counts;
}

/**
 * Rolls a set of statuses up into a single worst-case status, used to colour a
 * category header from the results it contains.
 */
export function aggregateStatus(
  results: readonly ConformanceResult[],
): ConformanceStatus {
  for (const status of STATUS_ORDER) {
    if (results.some((result) => result.status === status)) {
      return status;
    }
  }
  return 'pass';
}

/** A group of results sharing the same category, ready to render. */
export interface CategoryGroup {
  category: string;
  status: ConformanceStatus;
  counts: StatusCounts;
  results: ConformanceResult[];
}

/** Groups results by category, preserving first-seen category order. */
export function groupByCategory(report: ConformanceReport): CategoryGroup[] {
  const groups = new Map<string, ConformanceResult[]>();
  for (const result of report.results) {
    const bucket = groups.get(result.category);
    if (bucket) {
      bucket.push(result);
    } else {
      groups.set(result.category, [result]);
    }
  }

  return Array.from(groups, ([category, results]) => ({
    category,
    status: aggregateStatus(results),
    counts: countByStatus(results),
    results,
  }));
}

/** Fraction of results that passed, in the range [0, 1]. */
export function passRate(counts: StatusCounts): number {
  const total = counts.pass + counts.fail + counts.error + counts.skipped;
  return total === 0 ? 0 : counts.pass / total;
}
