/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import {
  ordinalForCteLabel,
  canonicalLabel,
  selectionForOrdinal,
  selectionForCteLabel,
  isRowSelected,
  isRangeSelected,
  buildPlanRowTree,
  CLEARED_SELECTION,
} from './searchLineage.ts';
import type { QueryPlan } from './searchTypes.ts';

const plan: QueryPlan = {
  explain: '',
  rows: [
    { label: 'cte0', canonicalLabel: 'cte0', kind: 'paramSource', body: 'ParamSource name', referencedCteIndexes: [] },
    { label: 'cte1', canonicalLabel: 'cte1', kind: 'paramSource', body: 'ParamSource gender', referencedCteIndexes: [] },
    { label: 'sort', canonicalLabel: 'sort', kind: 'sortSpec', body: 'SortSpec([], Valued)', referencedCteIndexes: [] },
  ],
  ctes: [
    { cteIndex: 0, parameterOrdinal: 0, contributingOrdinals: [0], span: null },
    { cteIndex: 1, parameterOrdinal: 1, contributingOrdinals: [1], span: null },
  ],
};

// A single-parameter chain: cte0 is the leaf ParamSource (ordinal 0), "root" is the ChainJoin wrapping it.
// PlanExplainer relabels the plan's match/output CTE as "root" -- here that's cteIndex 1, and its own
// CteProvenance.parameterOrdinal is null (the engine never attributes a structural CTE to one parameter
// directly). ContributingOrdinals is what the engine now closes this gap with -- read directly, not
// recomputed by walking referencedCteIndexes ourselves.
const chainPlan: QueryPlan = {
  explain: '',
  rows: [
    { label: 'cte0', canonicalLabel: 'cte0', kind: 'paramSource', body: 'StringSearchParam[2,2]  Text LIKE @p0', referencedCteIndexes: [] },
    { label: 'root', canonicalLabel: 'cte1', kind: 'chainJoin', body: 'ChainJoin(cte0, ref=1, inner=2, output=[1], Forward)', referencedCteIndexes: [0] },
  ],
  ctes: [
    { cteIndex: 0, parameterOrdinal: 0, contributingOrdinals: [0], span: null },
    { cteIndex: 1, parameterOrdinal: null, contributingOrdinals: [0], span: null },
  ],
};

// Intersect(cte0, cte1) combining two DIFFERENT parameters (ordinals 0 and 1) -- must stay unattributable
// (no shared ordinal) rather than arbitrarily picking one, but must still be selectable by its own identity.
const intersectPlan: QueryPlan = {
  explain: '',
  rows: [
    { label: 'cte0', canonicalLabel: 'cte0', kind: 'paramSource', body: 'StringSearchParam[1,1]  Text LIKE @p0', referencedCteIndexes: [] },
    { label: 'cte1', canonicalLabel: 'cte1', kind: 'paramSource', body: 'TokenSearchParam[1,2]  Code = @p1', referencedCteIndexes: [] },
    { label: 'root', canonicalLabel: 'cte2', kind: 'intersect', body: 'Intersect(cte0, cte1)', referencedCteIndexes: [0, 1] },
  ],
  ctes: [
    { cteIndex: 0, parameterOrdinal: 0, contributingOrdinals: [0], span: null },
    { cteIndex: 1, parameterOrdinal: 1, contributingOrdinals: [1], span: null },
    { cteIndex: 2, parameterOrdinal: null, contributingOrdinals: [0, 1], span: null },
  ],
};

test('a cte label resolves to its owning parameter ordinal', () => {
  assert.equal(ordinalForCteLabel(plan, 'cte1'), 1);
});

test('a non-cte label (sort/page/inc/countOnly) resolves to a null ordinal but still canonicalizes to itself', () => {
  assert.equal(ordinalForCteLabel(plan, 'sort'), null);
  assert.equal(canonicalLabel(plan, 'sort'), 'sort');
});

test('the "root" row resolves via its own CteProvenance when it has one', () => {
  const rootHasOwnOrdinal: QueryPlan = {
    explain: '',
    rows: [{ label: 'root', canonicalLabel: 'cte0', kind: 'paramSource', body: 'StringSearchParam[1,1]  Text LIKE @p0', referencedCteIndexes: [] }],
    ctes: [{ cteIndex: 0, parameterOrdinal: 3, contributingOrdinals: [3], span: null }],
  };
  assert.equal(ordinalForCteLabel(rootHasOwnOrdinal, 'root'), 3);
});

test('a structural "root" (ChainJoin) with no own ordinal reads the single ordinal its CteProvenance.contributingOrdinals closes over', () => {
  assert.equal(ordinalForCteLabel(chainPlan, 'root'), 0);
  assert.equal(ordinalForCteLabel(chainPlan, 'cte0'), 0);
});

test('a structural row referencing two DIFFERENT ordinals stays unattributable', () => {
  assert.equal(ordinalForCteLabel(intersectPlan, 'root'), null);
  assert.equal(canonicalLabel(intersectPlan, 'root'), 'cte2', 'it still has its own identity, just no owning parameter');
});

test('a plan row is selected when its cte maps to the selected ordinal', () => {
  assert.equal(isRowSelected('cte0', selectionForOrdinal(0), plan), true);
  assert.equal(isRowSelected('cte1', selectionForOrdinal(0), plan), false);
  assert.equal(isRowSelected('sort', selectionForOrdinal(0), plan), false);
});

test('a chained query\'s leaf and its "root" ChainJoin are selected together', () => {
  const selection = selectionForCteLabel(chainPlan, 'cte0');
  assert.equal(isRowSelected('cte0', selection, chainPlan), true);
  assert.equal(isRowSelected('root', selection, chainPlan), true);
});

test('nothing is selected when the selection is cleared', () => {
  assert.equal(isRowSelected('cte0', CLEARED_SELECTION, plan), false);
  assert.equal(isRangeSelected('cte0', CLEARED_SELECTION, plan), false);
});

test('a sql range is selected when its cte maps to the selected ordinal', () => {
  assert.equal(isRangeSelected('cte1', selectionForOrdinal(1), plan), true);
  assert.equal(isRangeSelected('cte1', selectionForOrdinal(0), plan), false);
});

test('a sql range labelled "cte1" (the chain\'s real numeric label -- SQL never uses "root") inherits like the row does', () => {
  const selection = selectionForCteLabel(chainPlan, 'cte0');
  assert.equal(isRangeSelected('cte1', selection, chainPlan), true);
});

test('clicking an unattributable structural row (Intersect over two different parameters) still selects and highlights only itself', () => {
  const selection = selectionForCteLabel(intersectPlan, 'root');
  // "root" canonicalizes to its real cteIndex-based label ("cte2") -- see the "root"/"cte{i}" alias test
  // below for why that canonicalization itself matters (the SQL range for this same CTE is "cte2", never
  // "root").
  assert.deepEqual(selection, { ordinal: null, label: 'cte2' });

  assert.equal(isRowSelected('root', selection, intersectPlan), true, 'the clicked row itself highlights');
  assert.equal(isRowSelected('cte0', selection, intersectPlan), false, 'its children do not -- no shared ordinal to group by');
  assert.equal(isRowSelected('cte1', selection, intersectPlan), false);
  assert.equal(isRangeSelected('cte2', selection, intersectPlan), true, 'its own generated SQL still highlights by cte identity');
});

test('a "root" plan row and its "cte{i}" SQL range are the same canonical identity (a click on either selects both)', () => {
  const fromRoot = selectionForCteLabel(intersectPlan, 'root');
  const fromRealLabel = selectionForCteLabel(intersectPlan, 'cte2');
  assert.deepEqual(fromRoot, fromRealLabel);
  assert.equal(isRowSelected('root', fromRealLabel, intersectPlan), true, 'clicking the SQL range selects the plan row too');
  assert.equal(isRangeSelected('cte2', fromRoot, intersectPlan), true, 'clicking the plan row selects the SQL range too');
});

test('an _include stage\'s main SQL range and its "lim" companion range are the same canonical identity', () => {
  const includePlan: QueryPlan = {
    explain: '',
    rows: [
      {
        label: 'inc0',
        canonicalLabel: 'inc0',
        kind: 'includeStage',
        body: 'IncludeStage(ref=1, seedTypes=[4], outputTypes=[1,2,3], seeds=[match], limit=0, Forward)',
        referencedCteIndexes: [],
      },
    ],
    ctes: [],
  };
  const selection = selectionForCteLabel(includePlan, 'inc0');
  assert.deepEqual(selection, { ordinal: null, label: 'inc0' });

  assert.equal(isRowSelected('inc0', selection, includePlan), true);
  assert.equal(isRangeSelected('inc0', selection, includePlan), true, 'the main SQL range highlights');
  assert.equal(isRangeSelected('inc0lim', selection, includePlan), true, 'its "lim" companion range highlights too, even though it has no plan row of its own');
  assert.equal(isRangeSelected('inc1lim', selection, includePlan), false, 'a different stage\'s "lim" range must not falsely match');
});

test('clicking a non-CTE row (e.g. an _include stage, _sort, paging, or countOnly) still selects and highlights itself and its own SQL', () => {
  // Regression coverage: these rows never had a cteIndex/ordinal at all (they aren't CTEs), so an earlier
  // design that only recognized CTE identity left them permanently unclickable despite each having a real,
  // separately-labelled range in the emitted SQL (confirmed live: "sort"/"inc0"/"page"/"countOnly" all get
  // their own SqlTextRange, same as any "cte{i}"). Plain label equality is what fixes this.
  const selection = selectionForCteLabel(plan, 'sort');
  assert.deepEqual(selection, { ordinal: null, label: 'sort' });

  assert.equal(isRowSelected('sort', selection, plan), true);
  assert.equal(isRangeSelected('sort', selection, plan), true, 'the sort stage\'s own SQL range highlights too');
  assert.equal(isRowSelected('cte0', selection, plan), false, 'unrelated rows do not light up');
});

test('selectionForOrdinal never carries a label, so a parameter click does not force-select an unrelated row', () => {
  assert.deepEqual(selectionForOrdinal(5), { ordinal: 5, label: null });
});

test('two unrelated leaf CTEs (no combinator referencing either) are each their own top-level tree', () => {
  const { tree, extras } = buildPlanRowTree(plan);
  assert.equal(tree.length, 2, 'cte0 and cte1 are both unreferenced by anything else, so both are their own top-level tree');
  assert.equal(tree[0].children.length, 0);
  assert.equal(tree[1].children.length, 0);
  assert.equal(extras.length, 1);
  assert.equal(extras[0].label, 'sort');
});

test('a chain nests its leaf ParamSource under the ChainJoin, not as a separate sibling', () => {
  const { tree } = buildPlanRowTree(chainPlan);
  assert.equal(tree.length, 1, 'only "root" is top-level -- cte0 is referenced by it, so it is not its own top-level entry');
  assert.equal(tree[0].row.label, 'root');
  assert.equal(tree[0].children.length, 1);
  assert.equal(tree[0].children[0].row.label, 'cte0');
  assert.equal(tree[0].children[0].children.length, 0);
});

test('an Intersect nests both of its distinct-parameter operands as children of one block', () => {
  const { tree } = buildPlanRowTree(intersectPlan);
  assert.equal(tree.length, 1);
  assert.equal(tree[0].row.label, 'root');
  assert.deepEqual(
    tree[0].children.map((c) => c.row.label),
    ['cte0', 'cte1'],
  );
});

test('a reference cycle does not infinite-loop and does not silently drop every row', () => {
  // Every CTE referencing every other is a pathological input a real compiler shouldn't produce (QueryPlan
  // is documented as a DAG), but this is display code: it must degrade safely, not crash or render nothing.
  const cyclicPlan: QueryPlan = {
    explain: '',
    rows: [
      { label: 'cte0', canonicalLabel: 'cte0', kind: 'union', body: 'Union(cte1)', referencedCteIndexes: [1] },
      { label: 'root', canonicalLabel: 'cte1', kind: 'union', body: 'Union(cte0)', referencedCteIndexes: [0] },
    ],
    ctes: [
      { cteIndex: 0, parameterOrdinal: null, contributingOrdinals: [], span: null },
      { cteIndex: 1, parameterOrdinal: null, contributingOrdinals: [], span: null },
    ],
  };
  const { tree } = buildPlanRowTree(cyclicPlan);
  // No node is ever "unreferenced" here, so the fallback treats every CTE as its own top-level entry --
  // both rows still appear (nothing is silently dropped), and neither re-descends into the other.
  assert.equal(tree.length, 2);
  const byLabel = new Map(tree.map((n) => [n.row.label, n]));
  assert.equal(byLabel.get('cte0')?.children.length, 1);
  assert.equal(byLabel.get('cte0')?.children[0].row.label, 'root');
  assert.equal(byLabel.get('cte0')?.children[0].children.length, 0, 'root -> cte0 would be a cycle, so it is skipped, not re-added');
  assert.equal(byLabel.get('root')?.children.length, 1);
  assert.equal(byLabel.get('root')?.children[0].row.label, 'cte0');
  assert.equal(byLabel.get('root')?.children[0].children.length, 0);
});

test('canonicalLabel resolves a SQL range labelled "cte{i}" even when no plan row is literally named that -- only "root" is', () => {
  // Regression: a naive `plan.rows.some(r => r.label === canonicalLabel(...))` check breaks here, because
  // the row at this cteIndex is called "root" in plan.rows, never "cte2" -- canonicalLabel itself must be
  // the source of truth for "does this resolve to something real", not a second lookup against row labels.
  assert.equal(canonicalLabel(intersectPlan, 'cte2'), 'cte2');
  assert.equal(canonicalLabel(intersectPlan, 'root'), 'cte2');
  assert.equal(intersectPlan.rows.some((r) => r.label === 'cte2'), false, 'confirms no row is literally labelled "cte2" -- "root" is the only row for that cte');
});

test('canonicalLabel returns null for SqlBuilder-internal sections with no plan-row counterpart at all (e.g. "orderBy"/"cteMatchPage"/"assembly")', () => {
  assert.equal(canonicalLabel(plan, 'orderBy'), null);
  assert.equal(canonicalLabel(plan, 'cteMatchPage'), null);
  assert.equal(canonicalLabel(plan, 'assembly'), null);
});

test('an unresolvable range never highlights, even when nothing else is selected either (both sides null must not accidentally match)', () => {
  const selection = selectionForOrdinal(0); // label is null here -- ordinal-only selection
  assert.equal(isRangeSelected('orderBy', selection, plan), false);
});
