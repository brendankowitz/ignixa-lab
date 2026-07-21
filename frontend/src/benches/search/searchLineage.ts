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

/** Extracts every `cte{i}` reference a structural row's body text names (e.g. `"ChainJoin(cte0, ref=1, ...)"`
 * -> `[0]`, `"Intersect(cte0, cte1)"` -> `[0, 1]`). These are the CTEs the row directly composes. */
function referencedCteIndexes(body: string): number[] {
  return [...body.matchAll(/cte(\d+)/g)].map((m) => Number(m[1]));
}

/** Resolves a row/range's own real CTE index: directly from a `"cte{i}"` label (SQL ranges are always this
 * shape), or — for the plan's relabelled `"root"` row — from the matching `PlanExplainRow.cteIndex` the
 * backend already computed. Null for a non-CTE label (inc{i}/sort/page/countOnly/orderBy/...). */
export function cteIndexForLabel(plan: QueryPlan, label: string): number | null {
  const match = /^cte(\d+)$/.exec(label);
  if (match) {
    return Number(match[1]);
  }
  return plan.rows.find((r) => r.label === label)?.cteIndex ?? null;
}

/**
 * The label identity to use when comparing two rows/ranges for "same underlying thing", or null when
 * `label` doesn't correspond to any real row/CTE at all (e.g. `"orderBy"` or `"cteMatchPage"` — SqlBuilder-
 * internal glue with nothing in `plan.rows[]` to highlight alongside). Most labels are already their own
 * stable identity, but two cases need normalizing to a shared one first:
 *
 * - `"root"` vs `"cte{i}"`: `PlanExplainer` relabels the plan's match/output CTE as `"root"` for the plan
 *   row, but the matching SQL range keeps that CTE's real `"cte{i}"` label — same CTE, different strings.
 *   Both canonicalize to `"cte{i}"`. Critically, `"cte{i}"` itself is *never* a literal `plan.rows[]` label
 *   in this case (that row is called `"root"`), so a caller checking "is this a real row?" must check the
 *   canonical *cte identity*, not do a naive `plan.rows.some(r => r.label === canonicalLabel(...))`.
 * - `"{base}lim"` vs `"{base}"`: `SqlBuilder` emits a second, unlabelled-in-the-plan CTE for an `_include`
 *   stage's page-limit check (e.g. `inc1lim`, alongside `inc1`) — two SQL ranges for the one plan row
 *   labelled `"inc1"`. Stripping a trailing `"lim"` and checking it against a real plan-row label folds
 *   that second range back into the same identity as its stage, rather than leaving it permanently
 *   unmatchable (it has no plan row of its own to canonicalize *to* otherwise).
 */
export function canonicalLabel(plan: QueryPlan, label: string): string | null {
  const index = cteIndexForLabel(plan, label);
  if (index !== null) {
    return `cte${index}`;
  }
  if (label.endsWith('lim')) {
    const base = label.slice(0, -'lim'.length);
    if (plan.rows.some((r) => r.label === base)) {
      return base;
    }
  }
  return plan.rows.some((r) => r.label === label) ? label : null;
}

/**
 * Resolves a plan-row / SQL-range label to the parameter ordinal that produced it, or null when it has no
 * single owning parameter (a non-CTE row, or a CTE this can't confidently attribute to one parameter).
 *
 * A leaf CTE (e.g. a `ParamSource`) carries its own `CteProvenance.parameterOrdinal` directly. A structural
 * CTE (`Intersect`/`Union`/`Except`/`ChainJoin`/`CompartmentSource`/`ResourceSource`) never does — the
 * engine only attributes a structural CTE to a single parameter when there plainly is one to attribute to,
 * and otherwise leaves it null (see `Ignixa.Search.Sql.Tracing.SearchCompiler.BuildPlanTrace`). For a
 * `ChainJoin`, that's a real gap for a lineage-highlighting UI: the whole chain is still one parameter's
 * expression, just lowered into two linked CTEs. So when a row's own ordinal is null, this inherits one
 * from the CTEs its body references — but only when every reference agrees on the exact same ordinal.
 * `Intersect`/`Union`/`Except` typically combine two genuinely different parameters, so they correctly stay
 * unresolved (null) here rather than arbitrarily highlighting as "one" parameter — see `Selection.label`
 * for how such a row (or a non-CTE row like `inc0`/`sort`, which never had an ordinal to begin with) still
 * gets to highlight itself.
 */
export function ordinalForCteLabel(plan: QueryPlan | null, label: string): number | null {
  if (!plan) {
    return null;
  }

  const index = cteIndexForLabel(plan, label);
  if (index === null) {
    return null;
  }

  const cte = plan.ctes.find((c) => c.cteIndex === index);
  if (cte && cte.parameterOrdinal !== null) {
    return cte.parameterOrdinal;
  }

  const row = plan.rows.find((r): r is PlanExplainRow => r.cteIndex === index);
  if (!row) {
    return null;
  }

  const referenced = referencedCteIndexes(row.body);
  if (referenced.length === 0) {
    return null;
  }

  const ordinals = new Set<number>();
  for (const refIndex of referenced) {
    const child = plan.ctes.find((c) => c.cteIndex === refIndex);
    if (!child || child.parameterOrdinal === null) {
      return null;
    }
    ordinals.add(child.parameterOrdinal);
  }
  return ordinals.size === 1 ? [...ordinals][0] : null;
}

/** The `Selection` a click on a parameter (Search / Search Expression column) produces — always ordinal-
 * only, since a parameter click has no single "this exact row/range" identity of its own. */
export function selectionForOrdinal(ordinal: number): Selection {
  return { ordinal, label: null };
}

/** The `Selection` a click on a plan row / SQL range labelled `label` produces: its inherited/own ordinal
 * when resolvable (so it still groups with the rest of its parameter's family), plus its own canonical
 * label unconditionally (so a row with no owning parameter at all — a structural CTE, or a non-CTE
 * result-shape row like `inc0`/`sort`/`page`/`countOnly` — still selects and highlights itself and its own
 * generated SQL). The label is canonicalized (see {@link canonicalLabel}) so clicking `"root"` or its real
 * `"cte{i}"` SQL range, or an include stage's main range or its `"lim"` companion, all produce the same
 * selection. Works for every row kind, not just CTEs — see {@link Selection}. */
export function selectionForCteLabel(plan: QueryPlan, label: string): Selection {
  return { ordinal: ordinalForCteLabel(plan, label), label: canonicalLabel(plan, label) };
}

/** Whether a plan row / SQL range with `label` belongs to the current selection — by shared ordinal (the
 * parameter-family join) or by canonical label identity (a row highlighting only itself, including its
 * `"lim"`-suffixed SQL companion and the `"root"`/`"cte{i}"` alias — see {@link canonicalLabel}). */
export function isRowSelected(label: string, selection: Selection, plan: QueryPlan | null): boolean {
  if (!plan || (selection.ordinal === null && selection.label === null)) {
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
  const cteRows = plan.rows.filter((r): r is PlanExplainRow & { cteIndex: number } => r.cteIndex !== null);
  const extras = plan.rows.filter((r) => r.cteIndex === null);
  const byIndex = new Map(cteRows.map((r) => [r.cteIndex, r]));

  const referencedAsChild = new Set<number>();
  for (const row of cteRows) {
    for (const childIndex of referencedCteIndexes(row.body)) {
      referencedAsChild.add(childIndex);
    }
  }

  function buildNode(row: PlanExplainRow & { cteIndex: number }, ancestors: ReadonlySet<number>): PlanRowNode {
    const children: PlanRowNode[] = [];
    for (const childIndex of referencedCteIndexes(row.body)) {
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

  const unreferenced = cteRows.filter((r) => !referencedAsChild.has(r.cteIndex));
  // A real compiler output always has exactly one CTE nothing else references (the plan's Match/output),
  // so `unreferenced` is normally non-empty. A genuine reference cycle among every CTE (shouldn't happen —
  // QueryPlan is documented as a DAG — but this is display code, not a place to silently drop rows on a
  // malformed input) would leave it empty; fall back to every CTE row as its own top-level entry rather
  // than rendering nothing.
  const topLevel = unreferenced.length > 0 ? unreferenced : cteRows;
  const tree = topLevel.map((r) => buildNode(r, new Set([r.cteIndex])));
  return { tree, extras };
}
