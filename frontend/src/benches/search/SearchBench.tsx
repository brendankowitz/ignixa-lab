import { useEffect, useState, type CSSProperties, type MouseEvent } from 'react';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { benchHeaderStyle, benchPageStyle, chipStyle, engineBadgeStyle, monoFont, sectionLabelStyle } from '../components/styles';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { useSearchTrace } from './useSearchTrace';
import { spanSegments, type Segment } from './searchSpans';
import { tokenizeSql } from './sqlHighlight';
import { SearchQueryBuilder } from './SearchQueryBuilder';
import {
  buildPlanRowTree,
  canonicalLabel,
  CLEARED_SELECTION,
  isRangeSelected,
  isRowSelected,
  ordinalForCteLabel,
  selectionForCteLabel,
  selectionForOrdinal,
  type PlanRowNode,
  type Selection,
} from './searchLineage';
import {
  DEFAULT_FHIR_VERSION,
  DEFAULT_QUERY,
  DEFAULT_RESOURCE_TYPE,
  FHIR_VERSIONS,
  RESOURCE_TYPES,
  type FhirVersion,
  type ParameterTrace,
  type PlanExplainRow,
  type QueryPlan,
  type ResourceType,
  type SqlTextRange,
} from './searchTypes';

const RESOURCE_TYPE_ITEMS: PillItem<ResourceType>[] = RESOURCE_TYPES.map((resourceType) => ({
  id: resourceType,
  label: resourceType,
}));

const FHIR_VERSION_ITEMS: PillItem<FhirVersion>[] = FHIR_VERSIONS.map((version) => ({ id: version, label: version }));

type SqlTab = 'sql' | 'explain';

const SQL_TAB_ITEMS: PillItem<SqlTab>[] = [
  { id: 'sql', label: 'SQL' },
  { id: 'explain', label: 'Explain' },
];

/** Stable, arbitrary-domain-string → chip-color mapping — IR/plan `kind`/`label` values come straight from the
 * engine, so unlike FhirPathBench's `astChipColors` we can't switch on a known set of expression types. */
const KIND_CHIP_COLORS: { bg: string; fg: string }[] = [
  { bg: 'var(--chip-teal-bg)', fg: 'var(--chip-teal-fg)' },
  { bg: 'var(--chip-vio-bg)', fg: 'var(--chip-vio-fg)' },
  { bg: 'var(--chip-amb-bg)', fg: 'var(--chip-amb-fg)' },
  { bg: 'var(--chip-pink-bg)', fg: 'var(--chip-pink-fg)' },
  { bg: 'var(--chip-ind-bg)', fg: 'var(--chip-ind-fg)' },
];

function kindChipColors(kind: string): { bg: string; fg: string } {
  let hash = 0;
  for (let i = 0; i < kind.length; i += 1) {
    hash = (hash * 31 + kind.charCodeAt(i)) >>> 0;
  }
  return KIND_CHIP_COLORS[hash % KIND_CHIP_COLORS.length];
}

/** Display text for a `PlanExplainRow.kind`/`PlanRowKind` token — the engine's tokens are camelCase wire
 * identifiers (`"chainJoin"`, `"includeStage"`), not display text. */
const PLAN_ROW_KIND_LABELS: Record<string, string> = {
  paramSource: 'ParamSource',
  intersect: 'Intersect',
  union: 'Union',
  resourceSource: 'ResourceSource',
  except: 'Except',
  chainJoin: 'ChainJoin',
  compartmentSource: 'CompartmentSource',
  includeStage: 'Include',
  sortSpec: 'Sort',
  pageSpec: 'Page',
  countOnly: 'CountOnly',
};

function planRowKindLabel(kind: string): string {
  return PLAN_ROW_KIND_LABELS[kind] ?? kind;
}

/** Hover text for a `SqlTextRange.kind`/`SqlRangeKind` token — every SQL segment carries one now, including
 * the structural sections with no plan row to join to (matchPage/where/seek/orderBy/assembly), so a segment
 * that isn't part of the click-to-trace lineage can still say what it is instead of being unlabeled glue. */
const SQL_RANGE_KIND_TITLES: Record<string, string> = {
  cte: 'CTE definition',
  matchPage: 'Match page (applies paging to the match set)',
  where: 'WHERE clause',
  seek: 'Keyset-seek predicate',
  orderBy: 'ORDER BY clause',
  include: 'Include stage',
  includeLimit: 'Include stage (limit-applying companion)',
  assembly: 'Final assembly (UNION ALL of match page + every include stage)',
};

/** Same left-to-right cut approach as `spanSegments`, but over the emitted SQL text and `SqlTextRange[]`
 * (start/length/label/kind, no tree — ranges don't nest for this DTO, but the algorithm tolerates it either
 * way). */
interface SqlSegment {
  start: number;
  length: number;
  label: string | null;
  kind: string | null;
}

function sqlSegments(sql: string, ranges: SqlTextRange[]): SqlSegment[] {
  const cuts = new Set<number>([0, sql.length]);
  const covering: { start: number; end: number; label: string; kind: string }[] = [];
  for (const range of ranges) {
    const start = Math.max(0, range.start);
    const end = Math.min(sql.length, range.start + range.length);
    if (end > start) {
      cuts.add(start);
      cuts.add(end);
      covering.push({ start, end, label: range.label, kind: range.kind });
    }
  }

  const boundaries = [...cuts].sort((a, b) => a - b);
  const segments: SqlSegment[] = [];
  for (let i = 0; i < boundaries.length - 1; i += 1) {
    const start = boundaries[i];
    const end = boundaries[i + 1];
    if (end <= start) {
      continue;
    }
    let best: { label: string; kind: string } | null = null;
    let bestWidth = Infinity;
    for (const candidate of covering) {
      if (candidate.start <= start && candidate.end >= end) {
        const width = candidate.end - candidate.start;
        if (width < bestWidth) {
          best = candidate;
          bestWidth = width;
        }
      }
    }
    segments.push({ start, length: end - start, label: best?.label ?? null, kind: best?.kind ?? null });
  }
  return segments;
}

/** Renders one span-segments run (a parameter's key or value string) as clickable/plain fragments. Every
 * clickable fragment shares the same owning ordinal, so they highlight together as a unit. */
function SegmentRun({
  text,
  segments,
  ordinal,
  selection,
  onSelect,
}: {
  text: string;
  segments: Segment[];
  ordinal: number;
  selection: Selection;
  onSelect: (ordinal: number) => void;
}) {
  const selected = selection.ordinal === ordinal;
  return (
    <>
      {segments.map((segment, index) => {
        const chunk = text.slice(segment.start, segment.start + segment.length);
        if (segment.node === null) {
          return (
            <span key={index} style={{ color: 'var(--text)' }}>
              {chunk}
            </span>
          );
        }
        return (
          <span
            key={index}
            onClick={(event: MouseEvent) => {
              event.stopPropagation();
              onSelect(ordinal);
            }}
            title={segment.node.kind}
            style={{
              cursor: 'pointer',
              borderRadius: 3,
              padding: '0 1px',
              textDecoration: 'underline dotted',
              textDecorationColor: 'var(--border2)',
              background: selected ? 'var(--accent-border)' : 'transparent',
              color: selected ? 'var(--accent)' : 'var(--text)',
            }}
          >
            {chunk}
          </span>
        );
      })}
    </>
  );
}

/** One "Search" column block: the parameter's detected search data type (mirrors the mock's per-span `cat`
 * category label — one per parameter block here rather than per token, since this column groups by
 * parameter, not by a single continuous flowing line), the key=value string rendered as clickable syntax
 * spans, plus an inline warning for `Ignored`/`Failed` outcomes (a per-parameter note, not a page-level
 * error). */
function SearchParamBlock({
  param,
  selection,
  onSelect,
}: {
  param: ParameterTrace;
  selection: Selection;
  onSelect: (ordinal: number) => void;
}) {
  const keySegments = spanSegments(param.key, param.keySyntax, 'Key');
  const valueSegments = spanSegments(param.value, param.valueSyntax, 'Value');
  const selected = selection.ordinal === param.ordinal;
  const muted = param.outcome.kind === 'Ignored';
  const failed = param.outcome.kind === 'Failed';

  return (
    <div
      style={{
        padding: '8px 10px',
        borderRadius: 8,
        border: `1px ${muted ? 'dashed' : 'solid'} ${failed ? 'var(--fail-border)' : selected ? 'var(--accent-border)' : 'var(--border2)'}`,
        background: selected ? 'var(--chip-vio-bg)' : 'var(--code)',
        opacity: muted ? 0.75 : 1,
        display: 'flex',
        flexDirection: 'column',
        gap: 4,
      }}
    >
      {param.dataType ? (
        <span
          style={{
            fontFamily: monoFont,
            fontSize: 8.5,
            letterSpacing: '.12em',
            textTransform: 'uppercase',
            fontWeight: 700,
            color: kindChipColors(param.dataType).fg,
          }}
        >
          {param.dataType}
        </span>
      ) : null}
      <div style={{ fontFamily: monoFont, fontSize: 12.5, display: 'flex', flexWrap: 'wrap' }}>
        <SegmentRun text={param.key} segments={keySegments} ordinal={param.ordinal} selection={selection} onSelect={onSelect} />
        <span style={{ color: 'var(--text4)' }}>=</span>
        <SegmentRun text={param.value} segments={valueSegments} ordinal={param.ordinal} selection={selection} onSelect={onSelect} />
      </div>
      {muted ? <span style={{ fontSize: 11, color: 'var(--text4)' }}>⚠ ignored — {param.outcome.reason}</span> : null}
      {failed ? (
        <span style={{ fontSize: 11, color: 'var(--fail)' }}>
          ✕ failed at {param.outcome.stage} — {param.outcome.reason}
        </span>
      ) : null}
    </div>
  );
}

/** One "Search Expression" column block: a parameter's `ir[]` as an indented kind-chip + text list. Each row
 * (not the block as a whole) is the click target, but the whole block highlights when its ordinal is selected. */
function ExpressionParamBlock({
  param,
  selection,
  onSelect,
  compact,
}: {
  param: ParameterTrace;
  selection: Selection;
  onSelect: (ordinal: number) => void;
  compact: boolean;
}) {
  const selected = selection.ordinal === param.ordinal;
  return (
    <div
      style={{
        borderRadius: 8,
        border: `1px solid ${selected ? 'var(--accent-border)' : 'var(--border2)'}`,
        background: selected ? 'var(--chip-vio-bg)' : 'var(--code)',
        padding: '6px 8px',
        display: 'flex',
        flexDirection: 'column',
        gap: 1,
      }}
    >
      <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text3)' }}>
        {param.key}={param.value}
      </span>
      {param.ir.map((row, index) => (
        <div
          key={index}
          onClick={(event: MouseEvent) => {
            event.stopPropagation();
            onSelect(param.ordinal);
          }}
          style={{
            display: 'flex',
            gap: 6,
            alignItems: 'baseline',
            padding: `2px 0 2px ${row.depth * (compact ? 10 : 16) + 2}px`,
            cursor: 'pointer',
            borderRadius: 4,
          }}
        >
          <span style={chipStyle(kindChipColors(row.kind).bg, kindChipColors(row.kind).fg)}>{row.kind}</span>
          <span style={{ fontFamily: monoFont, fontSize: 11.5, color: 'var(--text)' }}>{row.text}</span>
        </div>
      ))}
      {param.ir.length === 0 ? <span style={{ fontSize: 11, color: 'var(--text4)', padding: '2px 0' }}>no expression</span> : null}
    </div>
  );
}

/** One "SQL AST" column card, rendered once per node of the tree `buildPlanRowTree` produces (see
 * `PlanRowTree` below) — a chain's leaf and its `ChainJoin`, or an `Intersect`'s two operands, are nested
 * under their composing row rather than appearing as unrelated flat siblings, matching the mock's
 * `sqAstBlocksData` (each block carries multiple related lines, not one flat list). Card treatment matches
 * `ExpressionParamBlock`'s so the two columns read as the same visual language.
 *
 * Every row is clickable — CTE rows and non-CTE result-shape rows alike (`inc0`/`sort`/`page`/`countOnly`
 * all have their own real, labelled range in the emitted SQL, same as any CTE). A row with no resolvable
 * owning parameter (a genuine multi-parameter `Intersect`, a `ResourceSource` base set, or any non-CTE row
 * — none of those ever had one to begin with) still selects and highlights itself and its own generated SQL
 * (see `Selection.label` in `searchLineage.ts`), rendered with a dashed border — the same "system added, not
 * directly from one search parameter" treatment the Implicit chips use. Solid border means "this is exactly
 * one parameter's expression." */
function PlanRowView({
  row,
  plan,
  selection,
  onSelect,
}: {
  row: PlanExplainRow;
  plan: QueryPlan;
  selection: Selection;
  onSelect: (label: string) => void;
}) {
  const attributable = ordinalForCteLabel(plan, row.label) !== null;
  const selected = isRowSelected(row.label, selection, plan);
  return (
    <div
      onClick={(event: MouseEvent) => {
        event.stopPropagation();
        onSelect(row.label);
      }}
      title={attributable ? 'Select this parameter' : 'Select this SQL block (no single owning parameter)'}
      style={{
        display: 'flex',
        gap: 8,
        alignItems: 'baseline',
        flexWrap: 'wrap',
        padding: '6px 8px',
        borderRadius: 8,
        border: `1px ${attributable ? 'solid' : 'dashed'} ${selected ? 'var(--accent-border)' : 'var(--border2)'}`,
        background: selected ? 'var(--chip-vio-bg)' : 'var(--code)',
        opacity: attributable ? 1 : 0.85,
        cursor: 'pointer',
      }}
    >
      <span style={chipStyle(attributable ? 'var(--chip-teal-bg)' : 'var(--chip-gray-bg)', attributable ? 'var(--chip-teal-fg)' : 'var(--chip-gray2-fg)')}>
        {row.label}
      </span>
      <span style={chipStyle(kindChipColors(row.kind).bg, kindChipColors(row.kind).fg)}>{planRowKindLabel(row.kind)}</span>
      <span style={{ fontFamily: monoFont, fontSize: 11.5, color: 'var(--text)' }}>{row.body}</span>
    </div>
  );
}

/** Renders one `PlanRowNode` and, indented beneath it behind a guide line, every CTE it directly composes
 * — recursively, so a multi-level composition (e.g. a chain nested inside an `Intersect`) nests all the
 * way down. Each node keeps its own independent click/selected/dashed state (a chain's leaf and its
 * `ChainJoin` both solid and highlighting together; an `Intersect`'s two differently-owned operands each
 * on their own) — nesting only changes layout, not the click-to-trace semantics `PlanRowView` already has. */
function PlanRowTree({
  node,
  plan,
  selection,
  onSelect,
  compact,
}: {
  node: PlanRowNode;
  plan: QueryPlan;
  selection: Selection;
  onSelect: (label: string) => void;
  compact: boolean;
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <PlanRowView row={node.row} plan={plan} selection={selection} onSelect={onSelect} />
      {node.children.length > 0 ? (
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 6,
            marginLeft: compact ? 10 : 16,
            paddingLeft: 10,
            borderLeft: '2px solid var(--border2)',
          }}
        >
          {node.children.map((child, index) => (
            <PlanRowTree key={index} node={child} plan={plan} selection={selection} onSelect={onSelect} compact={compact} />
          ))}
        </div>
      ) : null}
    </div>
  );
}

/** Traces a FHIR search query (`GET /{resourceType}?{query}`) from parse through the lowered SQL plan to
 * generated SQL, with click-to-trace lineage across all views via a single `Selection`. */
export function SearchBench() {
  const stackedGrid = useIsNarrowViewport(900);
  const compact = useIsNarrowViewport(560);

  const [fhirVersion, setFhirVersion] = useState<FhirVersion>(DEFAULT_FHIR_VERSION);
  const [resourceType, setResourceType] = useState<ResourceType>(DEFAULT_RESOURCE_TYPE);
  const [query, setQuery] = useState(DEFAULT_QUERY);
  const [selection, setSelection] = useState<Selection>(CLEARED_SELECTION);
  const [sqlTab, setSqlTab] = useState<SqlTab>('sql');

  const { result, error, isLoading } = useSearchTrace(fhirVersion, resourceType, query);
  const plan = result?.plan ?? null;
  const emittedSql = result?.sql ?? null;
  const planRowTree = plan ? buildPlanRowTree(plan) : null;

  const selectOrdinal = (ordinal: number) => setSelection(selectionForOrdinal(ordinal));
  const selectCteLabel = (label: string) => {
    if (plan) {
      setSelection(selectionForCteLabel(plan, label));
    }
  };

  // Clicking a span sets `selection` to trace a parameter (or a self-contained structural CTE) across
  // columns, but that selection is only meaningful for the trace `result` it was clicked in. Two moments
  // can invalidate it:
  //  - the user edits fhirVersion/resourceType/query (a new request is about to be debounced/fetched), and
  //  - `result` itself swaps to a new reference once that debounced fetch actually resolves — which can land
  //    well after the reset above already fired, if the user clicks a span from the still-displayed *previous*
  //    result during the debounce/network window.
  // `useSearchTrace` only replaces `result` with a new object on a state update (fresh success, or cleared to
  // null on error) — it never mutates it in place and leaves it referentially untouched while a request is
  // merely in flight — so `result` is a safe, stable-until-changed effect dependency here.
  useEffect(() => {
    setSelection(CLEARED_SELECTION);
  }, [fhirVersion, resourceType, query, result]);

  const traceGridStyle: CSSProperties = {
    display: 'grid',
    gridTemplateColumns: stackedGrid ? '1fr' : 'repeat(3, minmax(0, 1fr))',
    gap: 14,
    alignItems: 'start',
  };

  const hasSelection = selection.ordinal !== null || selection.label !== null;

  return (
    <div style={benchPageStyle(1440, compact)}>
      <div style={benchHeaderStyle(compact)}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>Search</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>
          Trace a FHIR search query from parse to generated SQL, targeting the Microsoft FHIR Server-compatible schema.
        </span>
        <div style={{ flex: 1 }} />
        {plan ? <span style={chipStyle('var(--chip-vio-bg)', 'var(--chip-vio-fg)')}>{`${plan.ctes.length} CTEs`}</span> : null}
        <span style={engineBadgeStyle}>{isLoading ? 'tracing…' : 'ignixa-search'}</span>
      </div>

      <Card>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <span style={sectionLabelStyle}>FHIR version</span>
          <Pills items={FHIR_VERSION_ITEMS} activeId={fhirVersion} onChange={setFhirVersion} />
          <div style={{ width: 1, height: 18, background: 'var(--border2)' }} />
          <span style={sectionLabelStyle}>Resource type</span>
          <Pills items={RESOURCE_TYPE_ITEMS} activeId={resourceType} onChange={setResourceType} />
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span style={sectionLabelStyle}>Search query</span>
          <div
            style={{
              display: 'flex',
              alignItems: 'stretch',
              border: '1px solid var(--border2)',
              borderRadius: 8,
              background: 'var(--code)',
              minWidth: 0,
            }}
          >
            <span
              style={{
                fontFamily: monoFont,
                fontSize: 12.5,
                color: 'var(--text3)',
                padding: '11px 0 11px 13px',
                whiteSpace: 'nowrap',
                userSelect: 'none',
              }}
            >
              GET /{resourceType}?
            </span>
            <textarea
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              spellCheck={false}
              rows={2}
              aria-label="Search query"
              style={{
                flex: 1,
                minWidth: 0,
                border: 'none',
                outline: 'none',
                background: 'transparent',
                fontFamily: monoFont,
                fontSize: 12.5,
                lineHeight: 1.55,
                color: 'var(--text)',
                padding: '11px 13px 11px 4px',
                resize: 'vertical',
              }}
            />
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span style={sectionLabelStyle}>Builder</span>
          <SearchQueryBuilder resourceType={resourceType} query={query} onQueryChange={setQuery} />
        </div>

        {result && result.implicit.length > 0 ? (
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>Implicit</span>
            {result.implicit.map((implicit, index) => (
              <span key={index} title={implicit.reason} style={chipStyle('var(--chip-gray-bg)', 'var(--chip-gray2-fg)')}>
                {`${implicit.name}=${implicit.value}`}
              </span>
            ))}
          </div>
        ) : null}
      </Card>

      {error !== null ? <ErrorBanner message={error} /> : null}
      {result?.failure ? <ErrorBanner message={`${result.failure.stage}: ${result.failure.message}`} /> : null}

      {result ? (
        <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
          <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>
            {result.parameters.length} {result.parameters.length === 1 ? 'param' : 'params'}
          </span>
          {hasSelection ? (
            <span
              onClick={() => setSelection(CLEARED_SELECTION)}
              style={{ fontFamily: monoFont, fontSize: 11, fontWeight: 600, color: 'var(--accent)', cursor: 'pointer' }}
            >
              ✕ clear lineage highlight
            </span>
          ) : null}
        </div>
      ) : null}

      <div style={traceGridStyle} onClick={() => setSelection(CLEARED_SELECTION)}>
        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>Search</span>
          {result && result.parameters.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {result.parameters.map((param) => (
                <SearchParamBlock key={param.ordinal} param={param} selection={selection} onSelect={selectOrdinal} />
              ))}
            </div>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No parameters parsed yet.</span>
          )}
        </Card>

        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>Search Expression</span>
          {result && result.parameters.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {result.parameters.map((param) => (
                <ExpressionParamBlock key={param.ordinal} param={param} selection={selection} onSelect={selectOrdinal} compact={compact} />
              ))}
            </div>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No typed expression yet.</span>
          )}
        </Card>

        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>SQL AST</span>
          {plan && planRowTree ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {planRowTree.tree.map((node, index) => (
                <PlanRowTree key={index} node={node} plan={plan} selection={selection} onSelect={selectCteLabel} compact={compact} />
              ))}
              {planRowTree.extras.map((row, index) => (
                <PlanRowView key={`extra-${index}`} row={row} plan={plan} selection={selection} onSelect={selectCteLabel} />
              ))}
            </div>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No plan emitted yet.</span>
          )}
        </Card>
      </div>

      <Card>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <Pills items={SQL_TAB_ITEMS} activeId={sqlTab} onChange={setSqlTab} />
        </div>

        {sqlTab === 'sql' && emittedSql ? (
          <pre
            style={{
              margin: 0,
              fontFamily: monoFont,
              fontSize: 12,
              lineHeight: 1.6,
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
              color: 'var(--text)',
            }}
          >
            {sqlSegments(emittedSql.sql, emittedSql.ranges).map((segment, index) => {
              const text = emittedSql.sql.slice(segment.start, segment.start + segment.length);
              const label = segment.label;
              // Clickable whenever this SQL range resolves to a real canonical identity — covers every row
              // kind (cte0/root/inc0/sort/page/countOnly) and an include stage's "lim" companion range, not
              // just CTEs. `canonicalLabel` returns null for a range with no plan-row counterpart at all
              // (e.g. "orderBy"/"assembly", SqlBuilder-internal glue), which stays unclickable — but every
              // range now carries a `kind`, so even those get a tooltip instead of being unlabeled text.
              const clickable = plan !== null && label !== null && canonicalLabel(plan, label) !== null;
              const selected = clickable && label !== null && isRangeSelected(label, selection, plan);
              const title = clickable ? (label ?? undefined) : segment.kind ? SQL_RANGE_KIND_TITLES[segment.kind] : undefined;
              // Syntax color lives per-token (see tokenizeSql) rather than being overridden to a flat accent
              // color on selection — a segment is often a whole multi-line CTE body now, and a solid-color
              // block of that size would read worse than keeping keywords/strings/numbers distinguishable
              // under the selection background.
              return (
                <span
                  key={index}
                  onClick={clickable && label !== null ? () => selectCteLabel(label) : undefined}
                  title={title}
                  style={{
                    cursor: clickable ? 'pointer' : undefined,
                    borderRadius: 3,
                    background: selected ? 'var(--accent-border)' : 'transparent',
                  }}
                >
                  {tokenizeSql(text).map((token, tokenIndex) => (
                    <span key={tokenIndex} style={{ color: token.color }}>
                      {token.text}
                    </span>
                  ))}
                </span>
              );
            })}
          </pre>
        ) : null}
        {sqlTab === 'sql' && !emittedSql ? <span style={{ fontSize: 11, color: 'var(--text4)' }}>No SQL emitted yet.</span> : null}
        {sqlTab === 'explain' ? (
          <pre
            style={{
              margin: 0,
              fontFamily: monoFont,
              fontSize: 11.5,
              lineHeight: 1.6,
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
              color: 'var(--text2)',
            }}
          >
            {plan?.explain || 'No plan emitted yet.'}
          </pre>
        ) : null}
      </Card>
    </div>
  );
}
