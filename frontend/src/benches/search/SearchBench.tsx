import { useState, type CSSProperties, type MouseEvent } from 'react';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { benchHeaderStyle, benchPageStyle, chipStyle, engineBadgeStyle, monoFont, sectionLabelStyle } from '../components/styles';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { useSearchTrace } from './useSearchTrace';
import { spanSegments, type Segment } from './searchSpans';
import { isRangeSelected, isRowSelected, ordinalForCteLabel } from './searchLineage';
import {
  DEFAULT_QUERY,
  DEFAULT_RESOURCE_TYPE,
  EXAMPLE_QUERIES,
  RESOURCE_TYPES,
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

/** Same left-to-right cut approach as `spanSegments`, but over the emitted SQL text and `SqlTextRange[]`
 * (start/length/label, no tree — ranges don't nest for this DTO, but the algorithm tolerates it either way). */
interface SqlSegment {
  start: number;
  length: number;
  label: string | null;
}

function sqlSegments(sql: string, ranges: SqlTextRange[]): SqlSegment[] {
  const cuts = new Set<number>([0, sql.length]);
  const covering: { start: number; end: number; label: string }[] = [];
  for (const range of ranges) {
    const start = Math.max(0, range.start);
    const end = Math.min(sql.length, range.start + range.length);
    if (end > start) {
      cuts.add(start);
      cuts.add(end);
      covering.push({ start, end, label: range.label });
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
    let best: string | null = null;
    let bestWidth = Infinity;
    for (const candidate of covering) {
      if (candidate.start <= start && candidate.end >= end) {
        const width = candidate.end - candidate.start;
        if (width < bestWidth) {
          best = candidate.label;
          bestWidth = width;
        }
      }
    }
    segments.push({ start, length: end - start, label: best });
  }
  return segments;
}

/** Renders one span-segments run (a parameter's key or value string) as clickable/plain fragments. Every
 * clickable fragment shares the same owning ordinal, so they highlight together as a unit. */
function SegmentRun({
  text,
  segments,
  ordinal,
  selectedOrdinal,
  onSelect,
}: {
  text: string;
  segments: Segment[];
  ordinal: number;
  selectedOrdinal: number | null;
  onSelect: (ordinal: number) => void;
}) {
  const selected = selectedOrdinal === ordinal;
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

/** One "Search" column block: the parameter's key=value string rendered as clickable syntax spans, plus an
 * inline warning for `Ignored`/`Failed` outcomes (a per-parameter note, not a page-level error). */
function SearchParamBlock({
  param,
  selectedOrdinal,
  onSelect,
}: {
  param: ParameterTrace;
  selectedOrdinal: number | null;
  onSelect: (ordinal: number) => void;
}) {
  const keySegments = spanSegments(param.key, param.keySyntax, 'Key');
  const valueSegments = spanSegments(param.value, param.valueSyntax, 'Value');
  const selected = selectedOrdinal === param.ordinal;
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
      <div style={{ fontFamily: monoFont, fontSize: 12.5, display: 'flex', flexWrap: 'wrap' }}>
        <SegmentRun text={param.key} segments={keySegments} ordinal={param.ordinal} selectedOrdinal={selectedOrdinal} onSelect={onSelect} />
        <span style={{ color: 'var(--text4)' }}>=</span>
        <SegmentRun text={param.value} segments={valueSegments} ordinal={param.ordinal} selectedOrdinal={selectedOrdinal} onSelect={onSelect} />
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
  selectedOrdinal,
  onSelect,
  compact,
}: {
  param: ParameterTrace;
  selectedOrdinal: number | null;
  onSelect: (ordinal: number) => void;
  compact: boolean;
}) {
  const selected = selectedOrdinal === param.ordinal;
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

/** One "Lowered AST" column row: `Plan.Rows[]` is flat (no `depth` field, unlike `IrRow`), so rows render as a
 * single indented list rather than a recursive tree — clickable only when its label resolves to a CTE's owning
 * parameter ordinal (root/sort/page/countOnly/inc{i} rows are informational only). */
function PlanRowView({
  row,
  plan,
  selectedOrdinal,
  onSelect,
}: {
  row: PlanExplainRow;
  plan: QueryPlan;
  selectedOrdinal: number | null;
  onSelect: (ordinal: number) => void;
}) {
  const ordinal = ordinalForCteLabel(plan, row.label);
  const clickable = ordinal !== null;
  const selected = isRowSelected(row.label, selectedOrdinal, plan);
  return (
    <div
      onClick={
        clickable
          ? (event: MouseEvent) => {
              event.stopPropagation();
              onSelect(ordinal);
            }
          : undefined
      }
      title={clickable ? `Select parameter #${ordinal}` : undefined}
      style={{
        display: 'flex',
        gap: 8,
        alignItems: 'baseline',
        padding: '4px 6px',
        borderRadius: 6,
        cursor: clickable ? 'pointer' : 'default',
        background: selected ? 'var(--chip-vio-bg)' : 'transparent',
      }}
    >
      <span style={chipStyle(clickable ? 'var(--chip-teal-bg)' : 'var(--chip-gray-bg)', clickable ? 'var(--chip-teal-fg)' : 'var(--chip-gray2-fg)')}>
        {row.label}
      </span>
      <span style={{ fontFamily: monoFont, fontSize: 11.5, color: 'var(--text)' }}>{row.body}</span>
    </div>
  );
}

/** Traces a FHIR search query (`GET /{resourceType}?{query}`) from parse through the lowered SQL plan to
 * generated SQL, with click-to-trace lineage across all views via a single `selectedOrdinal`. */
export function SearchBench() {
  const stackedGrid = useIsNarrowViewport(900);
  const compact = useIsNarrowViewport(560);

  const [resourceType, setResourceType] = useState<ResourceType>(DEFAULT_RESOURCE_TYPE);
  const [query, setQuery] = useState(DEFAULT_QUERY);
  const [selectedOrdinal, setSelectedOrdinal] = useState<number | null>(null);
  const [sqlTab, setSqlTab] = useState<SqlTab>('sql');

  const { result, error, isLoading } = useSearchTrace(resourceType, query);
  const plan = result?.plan ?? null;
  const emittedSql = result?.sql ?? null;

  const traceGridStyle: CSSProperties = {
    display: 'grid',
    gridTemplateColumns: stackedGrid ? '1fr' : 'repeat(3, minmax(0, 1fr))',
    gap: 14,
    alignItems: 'start',
  };

  return (
    <div style={benchPageStyle(1440, compact)}>
      <div style={benchHeaderStyle(compact)}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>Search</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>Trace a FHIR search query from parse to generated SQL.</span>
        <div style={{ flex: 1 }} />
        {plan ? <span style={chipStyle('var(--chip-vio-bg)', 'var(--chip-vio-fg)')}>{`${plan.ctes.length} CTEs`}</span> : null}
        <span style={engineBadgeStyle}>{isLoading ? 'tracing…' : 'ignixa-search'}</span>
      </div>

      <Card>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
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

        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
          <span style={{ fontSize: 11, color: 'var(--text4)' }}>Examples</span>
          {EXAMPLE_QUERIES[resourceType].map((example) => (
            <button
              key={example}
              type="button"
              onClick={() => setQuery(example)}
              style={{
                fontFamily: monoFont,
                fontSize: 10.5,
                padding: '5px 10px',
                borderRadius: 99,
                border: '1px solid var(--border)',
                color: 'var(--text2)',
                cursor: 'pointer',
                background: 'var(--panel)',
              }}
            >
              {example.length > 44 ? `${example.slice(0, 44)}…` : example}
            </button>
          ))}
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

      <div style={traceGridStyle} onClick={() => setSelectedOrdinal(null)}>
        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>Search</span>
          {result && result.parameters.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {result.parameters.map((param) => (
                <SearchParamBlock key={param.ordinal} param={param} selectedOrdinal={selectedOrdinal} onSelect={setSelectedOrdinal} />
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
                <ExpressionParamBlock
                  key={param.ordinal}
                  param={param}
                  selectedOrdinal={selectedOrdinal}
                  onSelect={setSelectedOrdinal}
                  compact={compact}
                />
              ))}
            </div>
          ) : (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>No typed expression yet.</span>
          )}
        </Card>

        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>Lowered AST</span>
          {plan ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 1, padding: '2px 0' }}>
              {plan.rows.map((row, index) => (
                <PlanRowView key={index} row={row} plan={plan} selectedOrdinal={selectedOrdinal} onSelect={setSelectedOrdinal} />
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
              const ordinal = label ? ordinalForCteLabel(plan, label) : null;
              if (ordinal === null || label === null) {
                return <span key={index}>{text}</span>;
              }
              const selected = isRangeSelected(label, selectedOrdinal, plan);
              return (
                <span
                  key={index}
                  onClick={() => setSelectedOrdinal(ordinal)}
                  title={`${label} → parameter #${ordinal}`}
                  style={{
                    cursor: 'pointer',
                    borderRadius: 3,
                    background: selected ? 'var(--accent-border)' : 'transparent',
                    color: selected ? 'var(--accent)' : 'var(--text)',
                  }}
                >
                  {text}
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
