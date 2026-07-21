import type { QueryPlan } from './searchTypes';

/** Resolves a plan-row / SQL-range label of the form "cte{i}" to the parameter ordinal that produced it,
 * or null for any other label (root, inc{i}, sort, page, countOnly) or an unattributed CTE. */
export function ordinalForCteLabel(plan: QueryPlan | null, label: string): number | null {
  if (!plan) {
    return null;
  }
  const match = /^cte(\d+)$/.exec(label);
  if (!match) {
    return null;
  }
  const cteIndex = Number(match[1]);
  const cte = plan.ctes.find((c) => c.cteIndex === cteIndex);
  return cte?.parameterOrdinal ?? null;
}

/** Whether a plan row with `label` belongs to the currently selected parameter ordinal. */
export function isRowSelected(label: string, selectedOrdinal: number | null, plan: QueryPlan | null): boolean {
  return selectedOrdinal !== null && ordinalForCteLabel(plan, label) === selectedOrdinal;
}

/** Whether a SQL text range with `label` belongs to the currently selected parameter ordinal. Same join as
 * plan rows — SQL ranges use the identical "cte{i}" labelling. */
export function isRangeSelected(label: string, selectedOrdinal: number | null, plan: QueryPlan | null): boolean {
  return selectedOrdinal !== null && ordinalForCteLabel(plan, label) === selectedOrdinal;
}
