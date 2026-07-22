import type { PlanExplainRow, QueryPlan } from './searchTypes';

/** What's currently selected for click-to-trace highlighting. `ordinal` drives the Search and Search
 * Expression columns (null when the selection has no single owning parameter). `label` is the *canonical*
 * identity (see {@link canonicalLabel}) of the plan row / SQL range the user clicked (e.g. `"cte1"`,
 * `"inc0"`, `"sort"`) — this is what lets ANY row highlight itself and its own generated SQL even when it
 * has no owning parameter to group by: a genuine multi-parameter `Intersect`/`Union`, a `ResourceSource`
 * base set, or a result-shape modifier (`_include`'s `inc0`, `_sort`'s `sort`, paging's `page`,
 * `_summary=count`'s `countOnly`) — none of those are CTEs at all, so they never had an owning parameter to
 * begin with. Store the *canonical* label here, not the raw clicked one — see `selectionForCteLabel`. */
export interface Selection {
  ordinal: number | null;
  label: string | null;
}

export const CLEARED_SELECTION: Selection = { ordinal: null, label: null };

/** Whether `selection` represents "nothing selected". True for `CLEARED_SELECTION` itself, and equally true
 * for whatever `selectionForCteLabel` returns when its `label` doesn't resolve to anything real (see
 * {@link findRow}) — both fields collapse to null in that case too, since there's nothing left to group or
 * highlight by. Centralizing this check is what lets `isRowSelected` and a UI's own "is anything selected?"
 * query agree by construction instead of each independently re-deriving the same two-null condition (and
 * risking one of them drifting to treat an unresolvable selection as "selected"). */
export function isSelectionEmpty(selection: Selection): boolean {
  return selection.ordinal === null && selection.label === null;
}

/**
 * Finds the plan row addressed by `label`, whichever of its three possible forms `label` is:
 * - a row's own display `label` (e.g. `"root"`, the match/output CTE's display name), or
 * - a row's `canonicalLabel` (e.g. `"cte2"`), which is what a SQL range is always labelled, or
 * - for an `_include` stage's second SQL range specifically, `"{canonicalLabel}lim"` — the engine documents
 *   that range as the stage's limit-applying companion, sharing its stage's one plan row rather than
 *   getting a row of its own (see `Ignixa.Search.Sql.Builders.SqlLabels.IncludeLimitLabel`).
 *
 * Returns null when `label` matches none of those — a SqlBuilder-internal section with no plan row at all
 * (`orderBy`/`cteMatchPage`/`where`/`seek`/`assembly`), self-describing only via its own `SqlTextRange.kind`.
 */
function findRow(plan: QueryPlan, label: string): PlanExplainRow | null {
  return (
    plan.rows.find((r) => r.canonicalLabel === label || r.label === label) ??
    plan.rows.find((r) => r.kind === 'includeStage' && `${r.canonicalLabel}lim` === label) ??
    null
  );
}

/**
 * The label identity to use when comparing two rows/ranges for "same underlying thing", or null when
 * `label` doesn't correspond to any real row at all (see {@link findRow}).
 */
export function canonicalLabel(plan: QueryPlan, label: string): string | null {
  return findRow(plan, label)?.canonicalLabel ?? null;
}

/** A CTE row's own real 0-based index, parsed from its canonical label — null for a non-CTE row
 * (sort/page/countOnly/includeStage), whose canonical label never has this shape. */
function cteIndexOf(canonical: string): number | null {
  const match = /^cte(\d+)$/.exec(canonical);
  return match ? Number(match[1]) : null;
}

/**
 * Resolves a plan-row / SQL-range label to the parameter ordinal that produced it, or null when it has no
 * single owning parameter (a non-CTE row, or a CTE whose `CteProvenance.contributingOrdinals` draws from
 * more than one parameter — `Intersect`/`Union`/`Except` typically combine two genuinely different
 * parameters, so they correctly stay unresolved here rather than arbitrarily highlighting as "one"
 * parameter — see `Selection.label` for how such a row, or a non-CTE row like `inc0`/`sort` that never had
 * an ordinal to begin with, still gets to highlight itself).
 */
export function ordinalForCteLabel(plan: QueryPlan | null, label: string): number | null {
  if (!plan) {
    return null;
  }

  const row = findRow(plan, label);
  if (!row) {
    return null;
  }

  const cteIndex = cteIndexOf(row.canonicalLabel);
  if (cteIndex === null) {
    return null;
  }

  const cte = plan.ctes.find((c) => c.cteIndex === cteIndex);
  return cte && cte.contributingOrdinals.length === 1 ? cte.contributingOrdinals[0] : null;
}

/** The `Selection` a click on a parameter (Search / Search Expression column) produces — always ordinal-
 * only, since a parameter click has no single "this exact row/range" identity of its own. */
export function selectionForOrdinal(ordinal: number): Selection {
  return { ordinal, label: null };
}

/** The `Selection` a click on a plan row / SQL range labelled `label` produces: its owning ordinal when
 * resolvable (so it still groups with the rest of its parameter's family), plus its own canonical label
 * unconditionally (so a row with no owning parameter at all — a structural CTE, or a non-CTE result-shape
 * row like `inc0`/`sort`/`page`/`countOnly` — still selects and highlights itself and its own generated
 * SQL). The label is canonicalized (see {@link canonicalLabel}) so clicking `"root"` or its real `"cte{i}"`
 * SQL range, or an include stage's main range or its `"lim"` companion, all produce the same selection. */
export function selectionForCteLabel(plan: QueryPlan, label: string): Selection {
  return { ordinal: ordinalForCteLabel(plan, label), label: canonicalLabel(plan, label) };
}

/** Whether a plan row / SQL range with `label` belongs to the current selection — by shared ordinal (the
 * parameter-family join) or by canonical label identity (a row highlighting only itself, including its
 * `"lim"`-suffixed SQL companion and the `"root"`/`"cte{i}"` alias — see {@link canonicalLabel}). */
export function isRowSelected(label: string, selection: Selection, plan: QueryPlan | null): boolean {
  if (!plan || isSelectionEmpty(selection)) {
    return false;
  }
  if (selection.ordinal !== null && ordinalForCteLabel(plan, label) === selection.ordinal) {
    return true;
  }
  return selection.label !== null && canonicalLabel(plan, label) === selection.label;
}

/** Alias of {@link isRowSelected} — SQL ranges and plan rows resolve through the identical join. */
export const isRangeSelected = isRowSelected;

/** One CTE row plus its directly-referenced child CTEs, recursively. Lets the SQL AST column render a
 * chain's leaf + `ChainJoin` (or an `Intersect`'s two operands) as one nested block instead of unrelated
 * flat rows — matching the mock's `sqAstBlocksData`, whose blocks each carry multiple indented lines. */
export interface PlanRowNode {
  row: PlanExplainRow;
  children: PlanRowNode[];
}

/** Groups a plan's CTE rows into a forest of {@link PlanRowNode} trees (rooted at whichever CTEs are never
 * referenced by another CTE's body — normally just the plan's single match/output CTE) plus the non-CTE
 * rows (sort/page/inc{i}/countOnly) unchanged, since those aren't part of the CTE graph at all. */
export function buildPlanRowTree(plan: QueryPlan): { tree: PlanRowNode[]; extras: PlanExplainRow[] } {
  const cteRows = plan.rows.filter((r) => cteIndexOf(r.canonicalLabel) !== null);
  const extras = plan.rows.filter((r) => cteIndexOf(r.canonicalLabel) === null);
  const byIndex = new Map(cteRows.map((r) => [cteIndexOf(r.canonicalLabel) as number, r]));

  const referencedAsChild = new Set<number>();
  for (const row of cteRows) {
    for (const childIndex of row.referencedCteIndexes) {
      referencedAsChild.add(childIndex);
    }
  }

  function buildNode(row: PlanExplainRow, ancestors: ReadonlySet<number>): PlanRowNode {
    const children: PlanRowNode[] = [];
    for (const childIndex of row.referencedCteIndexes) {
      // A reference cycle shouldn't occur in a real query plan (it's a DAG by construction), but guard
      // against infinite recursion rather than trust that invariant blindly.
      if (ancestors.has(childIndex)) {
        continue;
      }
      const childRow = byIndex.get(childIndex);
      if (childRow) {
        children.push(buildNode(childRow, new Set(ancestors).add(childIndex)));
      }
    }
    return { row, children };
  }

  const unreferenced = cteRows.filter((r) => !referencedAsChild.has(cteIndexOf(r.canonicalLabel) as number));
  // A real compiler output always has exactly one CTE nothing else references (the plan's Match/output),
  // so `unreferenced` is normally non-empty. A genuine reference cycle among every CTE (shouldn't happen —
  // QueryPlan is documented as a DAG — but this is display code, not a place to silently drop rows on a
  // malformed input) would leave it empty; fall back to every CTE row as its own top-level entry rather
  // than rendering nothing.
  const topLevel = unreferenced.length > 0 ? unreferenced : cteRows;
  const tree = topLevel.map((r) => buildNode(r, new Set([cteIndexOf(r.canonicalLabel) as number])));
  return { tree, extras };
}
