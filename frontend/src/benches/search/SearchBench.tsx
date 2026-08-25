import { useEffect, useState, type CSSProperties, type MouseEvent, type ReactNode } from 'react';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { benchHeaderStyle, benchPageStyle, chipStyle, engineBadgeStyle, monoFont, sectionLabelStyle } from '../components/styles';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { useSearchTrace } from './useSearchTrace';
import { cutByCovers, spanSegments, type Cover, type Segment } from './searchSpans';
import { tokenizeSql } from './sqlHighlight';
import { SearchQueryBuilder } from './SearchQueryBuilder';
import {
  buildPlanRowTree,
  canonicalLabel,
  CLEARED_SELECTION,
  isRangeSelected,
  isRowSelected,
  isSelectionEmpty,
  ordinalForCteLabel,
  selectionForCteLabel,
  selectionForOrdinal,
  type PlanRowNode,
  type Selection,
} from './searchLineage';
import {
  compartmentMemberOptions,
  DEFAULT_FHIR_VERSION,
  DEFAULT_QUERY,
  DEFAULT_RESOURCE_TYPE,
  everythingTypeFilterOptions,
  FHIR_VERSIONS,
  isCompartmentRoot,
  RESOURCE_TYPES,
  searchModesFor,
  type CompartmentMemberType,
  type FhirVersion,
  type ParameterTrace,
  type PlanExplainRow,
  type QueryPlan,
  type ResourceType,
  type SearchMode,
  type SearchRequest,
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

const SEARCH_MODE_LABELS: Record<SearchMode, string> = {
  type: 'Type search',
  compartment: 'Compartment',
  everything: '$everything',
};

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

/** A node with more than this many direct children collapses by default — the case that motivates it is a
 * wildcard compartment search, whose `Union` fans out to dozens of structurally identical
 * `CompartmentSource` rows (one per distinct compartment search parameter, which the compiler groups down
 * from the far larger set of (resourceType, parameter) pairs). At or below this, seeing every child at once
 * is more useful than collapsing — an `Intersect`'s 2 operands, a chain's leaf + `ChainJoin`. */
const MANY_CHILDREN_THRESHOLD = 8;

/** A text-styled control. Rendered as a real `<button>` rather than a clickable `<span>` so it is reachable
 * by keyboard and announced as actionable — the styling is stripped back to look like inline text, matching
 * how `Pills` in `primitives.tsx` keeps its own controls operable. `aria-pressed` is set only for toggles;
 * one-shot actions leave it undefined. */
function TextAction({
  onPress,
  pressed,
  title,
  style,
  children,
}: {
  onPress: () => void;
  pressed?: boolean;
  title?: string;
  style?: CSSProperties;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      title={title}
      aria-pressed={pressed}
      onClick={(event) => {
        event.stopPropagation();
        onPress();
      }}
      style={{
        appearance: 'none',
        border: 'none',
        background: 'none',
        padding: 0,
        font: 'inherit',
        color: 'inherit',
        cursor: 'pointer',
        ...style,
      }}
    >
      {children}
    </button>
  );
}

/** A kind chip (colored via {@link kindChipColors}) shared by `ExpressionParamBlock`'s IR-row kind and
 * `PlanRowView`'s plan-row kind, so the color lookup happens once per chip instead of twice. */
function KindChip({ kind, label }: { kind: string; label: string }) {
  const colors = kindChipColors(kind);
  return <span style={chipStyle(colors.bg, colors.fg)}>{label}</span>;
}

/** The border/background treatment `ExpressionParamBlock` and `PlanRowView` both give their outer card,
 * driven by the same "is this selected" / "does this have a single owning parameter" state — kept as one
 * helper so the two don't drift on what "selected" looks like. */
function selectableCardStyle(selected: boolean, dashed = false): CSSProperties {
  return {
    borderRadius: 8,
    border: `1px ${dashed ? 'dashed' : 'solid'} ${selected ? 'var(--accent-border)' : 'var(--border2)'}`,
    background: selected ? 'var(--chip-vio-bg)' : 'var(--code)',
  };
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

/** Same left-to-right cut approach as `spanSegments` (via the shared `cutByCovers`), but over the emitted
 * SQL text and `SqlTextRange[]` (start/length/label/kind, no tree — ranges don't nest for this DTO, but the
 * algorithm tolerates it either way). */
interface SqlSegment {
  start: number;
  length: number;
  label: string | null;
  kind: string | null;
}

function sqlSegments(sql: string, ranges: SqlTextRange[]): SqlSegment[] {
  const covers: Cover<{ label: string; kind: string }>[] = ranges
    .map((range) => ({
      start: Math.max(0, range.start),
      end: Math.min(sql.length, range.start + range.length),
      payload: { label: range.label, kind: range.kind },
    }))
    .filter((cover) => cover.end > cover.start);

  return cutByCovers(sql.length, covers).map((segment) => ({
    start: segment.start,
    length: segment.length,
    label: segment.payload?.label ?? null,
    kind: segment.payload?.kind ?? null,
  }));
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
 * spans, plus an inline warning for `Ignored`/`KnownMiss`/`Failed` outcomes (a per-parameter note, not a
 * page-level error). */
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
  // Compiled, not dropped -- the query is well-formed and still runs, it's just structurally incapable of
  // returning a row for this parameter. Distinct from `muted`: not faded, since nothing was ignored here.
  const knownMiss = param.outcome.kind === 'KnownMiss';

  return (
    <div
      style={{
        padding: '8px 10px',
        borderRadius: 8,
        border: `1px ${muted ? 'dashed' : 'solid'} ${failed ? 'var(--fail-border)' : knownMiss ? 'var(--warn)' : selected ? 'var(--accent-border)' : 'var(--border2)'}`,
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
      {/* "Can never match" is a claim about a database, and this bench has none — InMemorySymbolResolver
          answers from a small stand-in system/unit table, so an ordinary-but-unlisted system (a local
          CodeSystem, most UCUM units) lands here too. The verdict is the real one a server gives for a system
          it has not indexed; the qualifier keeps it from reading as a judgement on the user's query. */}
      {knownMiss ? (
        <span style={{ fontSize: 11, color: 'var(--warn)' }}>
          ⚠ compiled — can never match — {param.outcome.reason}
          <span style={{ color: 'var(--text4)' }}> (resolved against this bench's stand-in lookup table, not a live index)</span>
        </span>
      ) : null}
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
        ...selectableCardStyle(selected),
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
          <KindChip kind={row.kind} label={row.kind} />
          {/* Same flex min-width trap as PlanRowView's body span -- a long typed-expression row (e.g. a
              multi-branch chain or a composite predicate) would otherwise push the card wider instead of
              wrapping inside it. */}
          <span style={{ fontFamily: monoFont, fontSize: 11.5, color: 'var(--text)', minWidth: 0, overflowWrap: 'anywhere' }}>
            {row.text}
          </span>
        </div>
      ))}
      {/* "no expression" and "couldn't describe it" are different answers and must not look alike: the
          backend degrades an undescribable IR to an empty list, so without this the pane would assert the
          parameter has no expression when it actually has one nobody could render. */}
      {param.ir.length === 0 ? (
        param.irUnavailableReason !== null ? (
          <span style={{ fontSize: 11, color: 'var(--warn)', padding: '2px 0', overflowWrap: 'anywhere' }}>
            ⚠ expression unavailable — {param.irUnavailableReason}
          </span>
        ) : (
          <span style={{ fontSize: 11, color: 'var(--text4)', padding: '2px 0' }}>no expression</span>
        )
      ) : null}
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
        ...selectableCardStyle(selected, !attributable),
        display: 'flex',
        gap: 8,
        alignItems: 'baseline',
        flexWrap: 'wrap',
        padding: '6px 8px',
        opacity: attributable ? 1 : 0.85,
        cursor: 'pointer',
      }}
    >
      <span style={chipStyle(attributable ? 'var(--chip-teal-bg)' : 'var(--chip-gray-bg)', attributable ? 'var(--chip-teal-fg)' : 'var(--chip-gray2-fg)')}>
        {row.label}
      </span>
      <KindChip kind={row.kind} label={planRowKindLabel(row.kind)} />
      {/* A flex item's default min-width is its unwrapped content width, not 0 -- without min-width: 0 a long
          body (e.g. the many-way Union(cte0, cte1, ...) on a wildcard compartment search) pushes the row
          wider instead of wrapping inside the card. */}
      <span
        style={{
          fontFamily: monoFont,
          fontSize: 11.5,
          color: 'var(--text)',
          minWidth: 0,
          overflowWrap: 'anywhere',
        }}
      >
        {row.body}
      </span>
    </div>
  );
}

/** Whether `node` or any of its descendants is the current selection — used to force a collapsed node open
 * when the thing the user actually selected (e.g. by clicking a `cte{i}` range in the SQL pane) lives inside
 * it. Without this, collapsing could make a real, already-selected row unreachable/invisible in this pane. */
function subtreeContainsSelection(node: PlanRowNode, selection: Selection, plan: QueryPlan): boolean {
  if (isRowSelected(node.row.label, selection, plan)) {
    return true;
  }
  return node.children.some((child) => subtreeContainsSelection(child, selection, plan));
}

/** Renders one `PlanRowNode` and, indented beneath it behind a guide line, every CTE it directly composes
 * — recursively, so a multi-level composition (e.g. a chain nested inside an `Intersect`) nests all the
 * way down. Each node keeps its own independent click/selected/dashed state (a chain's leaf and its
 * `ChainJoin` both solid and highlighting together; an `Intersect`'s two differently-owned operands each
 * on their own) — nesting only changes layout, not the click-to-trace semantics `PlanRowView` already has.
 *
 * The guide line below a node recolors to the accent whenever THAT node (`node.row`) is itself selected —
 * reusing the exact same `isRowSelected` join `PlanRowView` uses for the row, so the line lights up in
 * lockstep with it: a chain's leaf and its `ChainJoin` share an ordinal, so selecting either highlights the
 * `ChainJoin`'s own row (and thus its line) too, visibly tying the leaf to the join it belongs to. An
 * `Intersect`'s two operands don't share an ordinal with each other or with the `Intersect` itself, so
 * clicking one operand alone leaves the line uncolored — only clicking the `Intersect` row directly lights
 * up the line grouping its two children, which is the one click that actually asserts "these are one
 * group."
 *
 * A node past `MANY_CHILDREN_THRESHOLD` direct children (a wildcard compartment search's Union of dozens of
 * near-identical leaf CTEs) starts collapsed behind a one-line summary instead of dumping every child card —
 * expandable on click, or automatically if the current selection lives inside it (see
 * `subtreeContainsSelection`), so nothing already selected ever goes invisible. */
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
  const groupSelected = isRowSelected(node.row.label, selection, plan);
  const manyChildren = node.children.length > MANY_CHILDREN_THRESHOLD;
  const [manuallyExpanded, setManuallyExpanded] = useState(false);

  // Positional keys can't tell one trace's "cte5" from another's, so a node the user expanded in the previous
  // query would stay expanded at the same position in the next one. Collapse back to the auto-collapse
  // default whenever the plan itself changes.
  useEffect(() => {
    setManuallyExpanded(false);
  }, [plan]);
  const expanded = !manyChildren || manuallyExpanded || node.children.some((child) => subtreeContainsSelection(child, selection, plan));

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
            borderLeft: `2px solid ${groupSelected ? 'var(--accent-border)' : 'var(--border2)'}`,
          }}
        >
          {expanded ? (
            node.children.map((child) => (
              // Keyed by canonical label, not array index, so a node keeps its identity when siblings are
              // reordered or inserted within one plan rather than every position past the change re-keying.
              // Note this does NOT isolate one trace from the next: canonical labels are positional CTE names
              // ("cte0", "cte1", ...), so "cte5" collides across traces exactly as index 5 would. The
              // manuallyExpanded reset below is what actually handles a new plan.
              <PlanRowTree key={child.row.canonicalLabel} node={child} plan={plan} selection={selection} onSelect={onSelect} compact={compact} />
            ))
          ) : (
            <TextAction
              onPress={() => setManuallyExpanded(true)}
              style={{ fontFamily: monoFont, fontSize: 11.5, color: 'var(--accent)', textAlign: 'left' }}
            >
              ▸ {node.children.length} sources (expand)
            </TextAction>
          )}
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

  const [searchMode, setSearchMode] = useState<SearchMode>('type');
  // Pre-filled, not blank -- an empty id silently traces nothing and leaves every pane reading "No X emitted
  // yet.", which is easy to mistake for a broken feature rather than a field waiting for input.
  const [compartmentId, setCompartmentId] = useState('example');
  const [memberType, setMemberType] = useState<CompartmentMemberType>('*');
  const [everythingId, setEverythingId] = useState('example');
  const [typeFilter, setTypeFilter] = useState<ResourceType[]>([]);
  const [since, setSince] = useState('');
  const [everythingStart, setEverythingStart] = useState('');
  const [everythingEnd, setEverythingEnd] = useState('');
  const [includeReferencedResources, setIncludeReferencedResources] = useState(true);

  const availableModes = searchModesFor(resourceType);
  const handleResourceTypeChange = (nextType: ResourceType) => {
    setResourceType(nextType);
    if (!searchModesFor(nextType).includes(searchMode)) {
      setSearchMode('type');
    }
    // Member types are per-root (a Patient is not in the Encounter compartment), so a carried-over
    // memberType can be absent from the new root's options -- which renders the Pills row with nothing
    // highlighted while still issuing requests for the stale type. Fall back to the wildcard, which every
    // root offers.
    if (isCompartmentRoot(nextType) && !compartmentMemberOptions(nextType).includes(memberType)) {
      setMemberType('*');
    }
  };

  // Which resource type the query string is compiled against, and so which parameters the Builder should
  // offer. In a scoped compartment search that is the *member* type, not the root: the backend compiles
  // `Patient/{id}/Observation?...` against Observation, so offering Patient's search parameters here would
  // suggest terms that silently compile to nothing. A wildcard search has no single member type and does
  // compile against the root.
  const queryResourceType: ResourceType =
    searchMode === 'compartment' && memberType !== '*' ? memberType : resourceType;

  const searchRequest: SearchRequest | null = (() => {
    if (searchMode === 'type') {
      return { mode: 'type', fhirVersion, resourceType, query };
    }
    if (searchMode === 'compartment') {
      if (!compartmentId.trim() || !isCompartmentRoot(resourceType)) {
        return null;
      }
      return { mode: 'compartment', fhirVersion, compartmentType: resourceType, compartmentId: compartmentId.trim(), memberType, query };
    }
    if (!everythingId.trim()) {
      return null;
    }
    return {
      mode: 'everything',
      fhirVersion,
      patientId: everythingId.trim(),
      typeFilter,
      since,
      start: everythingStart,
      end: everythingEnd,
      includeReferencedResources,
    };
  })();

  const searchRequestKey = JSON.stringify(searchRequest);

  const { result, error, isLoading } = useSearchTrace(searchRequest);
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
  //  - the user edits any input that produces a new request (a fetch is about to be debounced), and
  //  - `result` itself swaps to a new reference once that debounced fetch actually resolves — which can land
  //    well after the reset above already fired, if the user clicks a span from the still-displayed *previous*
  //    result during the debounce/network window.
  // `useSearchTrace` only replaces `result` with a new object on a state update (fresh success, or cleared to
  // null on error) — it never mutates it in place and leaves it referentially untouched while a request is
  // merely in flight — so `result` is a safe, stable-until-changed effect dependency here.
  //
  // Keyed off the serialized request rather than a hand-listed dep set: mode, compartment id, member type,
  // the $everything id and its five filters all change the request too, and enumerating them here means
  // every future input is one someone has to remember to add. This is the same key `useSearchTrace` debounces
  // on, so the reset fires exactly when a refetch does, never on an unrelated re-render.
  useEffect(() => {
    setSelection(CLEARED_SELECTION);
  }, [searchRequestKey, result]);

  const traceGridStyle: CSSProperties = {
    display: 'grid',
    gridTemplateColumns: stackedGrid ? '1fr' : 'repeat(3, minmax(0, 1fr))',
    gap: 14,
    alignItems: 'start',
  };

  const hasSelection = !isSelectionEmpty(selection);

  // The "Search query" block this labels is hidden entirely in $everything mode (which has no query string),
  // so there is no 'everything' arm here.
  const searchQueryBreadcrumb =
    searchMode === 'compartment'
      ? `GET /${resourceType}/${compartmentId.trim() || '{id}'}/${memberType}?`
      : `GET /${resourceType}?`;

  // Non-null exactly when the compartment controls should render. Narrowing `resourceType` here (rather than
  // at each use) is what lets `compartmentMemberOptions` take the stricter `CompartmentRoot`.
  const compartmentRoot = isCompartmentRoot(resourceType) ? resourceType : null;

  // Why nothing is being traced right now, or null when a request is in flight or done. Distinct from
  // `error`: this is "waiting for you", not "something went wrong", and without it every pane falls back to
  // its first-paint placeholder, which reads as a broken bench rather than an empty field.
  const notReadyReason =
    searchRequest !== null
      ? null
      : searchMode === 'compartment'
        ? `Enter a ${resourceType} id to trace a compartment search.`
        : 'Enter a Patient id to trace $everything.';

  // Keyed off the mode, not off `parameters.length === 0`: a plain type search with an empty query box also
  // returns zero parameters, and telling the user that is "a whole-compartment operation" is simply false.
  const emptyParametersNote =
    searchMode === 'everything'
      ? 'No parameters — $everything is one whole operation, not a parameter-driven query.'
      : searchMode === 'compartment'
        ? 'No parameters — add a query above to filter within the compartment.'
        : 'No parameters — add a query above.';

  return (
    <div style={benchPageStyle(1440, compact)}>
      <div style={benchHeaderStyle(compact)}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>Search</h1>
        <span title="Powered by Ignixa.Search.Sql, an alpha/experimental package — behavior and output shape may still change." style={chipStyle('var(--chip-amb-bg)', 'var(--chip-amb-fg)')}>
          alpha
        </span>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>
          Trace a FHIR search query from parse to generated SQL, targeting the Microsoft FHIR Server-compatible schema.
        </span>
        <div style={{ flex: 1 }} />
        {plan ? (
          <span style={chipStyle('var(--chip-vio-bg)', 'var(--chip-vio-fg)')}>{`${plan.ctes.length} ${plan.ctes.length === 1 ? 'CTE' : 'CTEs'}`}</span>
        ) : null}
        <span style={engineBadgeStyle}>{isLoading ? 'tracing…' : 'ignixa-search'}</span>
      </div>

      <Card>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <span style={sectionLabelStyle}>FHIR version</span>
          <Pills items={FHIR_VERSION_ITEMS} activeId={fhirVersion} onChange={setFhirVersion} />
        </div>

        {availableModes.length > 1 ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <span style={sectionLabelStyle}>Mode</span>
            <Pills
              items={availableModes.map((mode) => ({ id: mode, label: SEARCH_MODE_LABELS[mode] }))}
              activeId={searchMode}
              onChange={setSearchMode}
            />
          </div>
        ) : null}

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <span style={sectionLabelStyle}>Resource type</span>
          <Pills items={RESOURCE_TYPE_ITEMS} activeId={resourceType} onChange={handleResourceTypeChange} />
        </div>

        {searchMode === 'compartment' && compartmentRoot ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <label style={sectionLabelStyle} htmlFor="compartment-id">{resourceType} id</label>
            <input
              id="compartment-id"
              value={compartmentId}
              onChange={(event) => setCompartmentId(event.target.value)}
              placeholder="example"
              spellCheck={false}
              style={{
                fontFamily: monoFont,
                fontSize: 12.5,
                padding: '6px 10px',
                borderRadius: 6,
                border: '1px solid var(--border2)',
                background: 'var(--code)',
                color: 'var(--text)',
                width: 140,
              }}
            />
            <TextAction
              onPress={() => setCompartmentId('example')}
              title="Reset to the sample id"
              style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--accent)' }}
            >
              example
            </TextAction>
            <div style={{ width: 1, height: 18, background: 'var(--border2)' }} />
            <span style={sectionLabelStyle}>within compartment, search</span>
            <Pills
              items={compartmentMemberOptions(compartmentRoot).map((type) => ({ id: type, label: type === '*' ? '* all types' : type }))}
              activeId={memberType}
              onChange={setMemberType}
            />
          </div>
        ) : null}

        {searchMode === 'everything' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <label style={sectionLabelStyle} htmlFor="everything-patient-id">Patient id</label>
              <input
                id="everything-patient-id"
                value={everythingId}
                onChange={(event) => setEverythingId(event.target.value)}
                placeholder="example"
                spellCheck={false}
                style={{
                  fontFamily: monoFont,
                  fontSize: 12.5,
                  padding: '6px 10px',
                  borderRadius: 6,
                  border: '1px solid var(--border2)',
                  background: 'var(--code)',
                  color: 'var(--text)',
                  width: 140,
                }}
              />
              <TextAction
                onPress={() => setEverythingId('example')}
                title="Reset to the sample id"
                style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--accent)' }}
              >
                example
              </TextAction>
            </div>
            {/* A multi-select, so each chip is an independently toggleable button carrying its own pressed
                state rather than one of a set of mutually exclusive Pills. */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }} role="group" aria-label="_type filter">
              <span style={sectionLabelStyle}>_type</span>
              {everythingTypeFilterOptions().map((type) => {
                const active = typeFilter.includes(type);
                return (
                  <TextAction
                    key={type}
                    pressed={active}
                    onPress={() =>
                      setTypeFilter((prev) => (active ? prev.filter((t) => t !== type) : [...prev, type]))
                    }
                    style={chipStyle(
                      active ? 'var(--chip-vio-bg)' : 'var(--chip-gray-bg)',
                      active ? 'var(--chip-vio-fg)' : 'var(--chip-gray2-fg)',
                    )}
                  >
                    {type}
                  </TextAction>
                );
              })}
              {typeFilter.length === 0 ? <span style={{ fontSize: 11, color: 'var(--text4)' }}>(all types)</span> : null}
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <label style={sectionLabelStyle} htmlFor="everything-since">_since</label>
              <input
                id="everything-since"
                value={since}
                onChange={(event) => setSince(event.target.value)}
                placeholder="2026-01-01T00:00:00Z"
                spellCheck={false}
                style={{ fontFamily: monoFont, fontSize: 11.5, padding: '5px 8px', borderRadius: 6, border: '1px solid var(--border2)', background: 'var(--code)', color: 'var(--text)', width: 190 }}
              />
              <label style={sectionLabelStyle} htmlFor="everything-start">start</label>
              <input
                id="everything-start"
                value={everythingStart}
                onChange={(event) => setEverythingStart(event.target.value)}
                placeholder="2020-01-01T00:00:00Z"
                spellCheck={false}
                style={{ fontFamily: monoFont, fontSize: 11.5, padding: '5px 8px', borderRadius: 6, border: '1px solid var(--border2)', background: 'var(--code)', color: 'var(--text)', width: 190 }}
              />
              <label style={sectionLabelStyle} htmlFor="everything-end">end</label>
              <input
                id="everything-end"
                value={everythingEnd}
                onChange={(event) => setEverythingEnd(event.target.value)}
                placeholder="2026-01-01T00:00:00Z"
                spellCheck={false}
                style={{ fontFamily: monoFont, fontSize: 11.5, padding: '5px 8px', borderRadius: 6, border: '1px solid var(--border2)', background: 'var(--code)', color: 'var(--text)', width: 190 }}
              />
            </div>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 11.5, color: 'var(--text3)', cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={includeReferencedResources}
                onChange={(event) => setIncludeReferencedResources(event.target.checked)}
              />
              include referenced resources (Practitioner / Organization / Location / Medication)
            </label>
          </div>
        ) : null}

        {searchMode !== 'everything' ? (
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
                {searchQueryBreadcrumb}
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
        ) : null}

        {searchMode !== 'everything' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <span style={sectionLabelStyle}>
              Builder{queryResourceType !== resourceType ? ` — ${queryResourceType} parameters` : ''}
            </span>
            {/* queryResourceType, not resourceType: in a scoped compartment search the query string is
                compiled against the member type, so offering the root's parameters would suggest terms that
                compile to nothing. */}
            <SearchQueryBuilder resourceType={queryResourceType} query={query} onQueryChange={setQuery} />
          </div>
        ) : null}

        {notReadyReason ? (
          <span style={{ fontSize: 11.5, color: 'var(--text4)' }}>{notReadyReason}</span>
        ) : null}

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
      {result?.failure ? (
        <ErrorBanner
          message={`${result.failure.scope} ${result.failure.stage}${result.failure.parameterCode ? ` (${result.failure.parameterCode})` : ''}: ${result.failure.message}`}
        />
      ) : null}
      {/* The backend answers an unrecognized FHIR version with an R4 trace rather than a 400, and reports the
          version it actually compiled against. Unreachable from the version pills alone, but the response
          carries the field precisely so a substitution can't pass as the version that was asked for — so say
          it rather than labelling someone else's trace with the version they picked. */}
      {result && result.fhirVersion !== fhirVersion ? (
        <ErrorBanner message={`Traced against ${result.fhirVersion}, not ${fhirVersion} — the backend does not recognize ${fhirVersion} and fell back.`} />
      ) : null}

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
          ) : result ? (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>{emptyParametersNote}</span>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>{notReadyReason ?? 'No parameters parsed yet.'}</span>
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
          ) : result ? (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>{emptyParametersNote}</span>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>{notReadyReason ?? 'No typed expression yet.'}</span>
          )}
        </Card>

        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>SQL AST</span>
          {plan && planRowTree ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {planRowTree.tree.map((node) => (
                <PlanRowTree key={node.row.canonicalLabel} node={node} plan={plan} selection={selection} onSelect={selectCteLabel} compact={compact} />
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
        {/* Every value the user typed reaches the SQL as a bind marker, so the statement above shows @p0/@p1
            and nothing else. Without this table the pane can show a compartment id or a $everything window
            bound to markers the reader cannot resolve — the provenance stops one step short of the values. */}
        {sqlTab === 'sql' && emittedSql && emittedSql.parameters.length > 0 ? (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px 14px', marginTop: 10, paddingTop: 8, borderTop: '1px solid var(--border2)' }}>
            {emittedSql.parameters.map((parameter) => (
              <span key={parameter.name} style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>
                <span style={{ color: 'var(--accent)' }}>{parameter.name}</span>
                {' = '}
                {parameter.value ?? 'null'}
              </span>
            ))}
          </div>
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
