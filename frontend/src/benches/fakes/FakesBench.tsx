import { useEffect, useMemo, useState, type CSSProperties } from 'react';
import { Card, ErrorBanner, Pills, Toggle, type PillItem } from '../components/primitives';
import { HighlightedJsonBlock } from '../components/HighlightedJsonBlock';
import {
  chipStyle,
  engineBadgeStyle,
  monoFont,
  monoInputStyle,
  primaryButtonStyle,
  sectionLabelStyle,
} from '../components/styles';
import { useCopyToClipboard } from '../../hooks/useCopyToClipboard';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { COPY_FEEDBACK_DURATION_MS, type FakesShareState } from '../../lib/shareLinks';
import { describeEdgeCase } from './edgeCaseDescriptions';
import { describeScenario } from './scenarioDescriptions';
import { generatePopulation, generateResource, generateScenario, getFakesMetadata } from './fakesApi';
import type {
  EdgeCaseFamilyMetadata,
  FakesMetadata,
  PopulationResult,
  ResourceResult,
  ScenarioResult,
} from './fakesTypes';

type FakesMode = 'population' | 'scenario' | 'resource';
type TargetBench = 'fhirpath' | 'fml' | 'sqlonfhir';
type OnSend = (targetBench: TargetBench, payload: Record<string, unknown> | Record<string, unknown>[], label: string) => void;

const MODE_ITEMS: PillItem<FakesMode>[] = [
  { id: 'population', label: 'Population' },
  { id: 'scenario', label: 'Scenario' },
  { id: 'resource', label: 'Resource' },
];

const DENSITY_ITEMS: PillItem<string>[] = [
  { id: 'Minimal', label: 'Minimal' },
  { id: 'Maximum', label: 'Maximum' },
];

/** Resource types promoted to inline pills; the rest live behind the "More" picker. Filtered to what the backend actually reports. */
const COMMON_RESOURCE_TYPES = ['Patient', 'Observation', 'Condition', 'Encounter', 'MedicationRequest', 'Procedure'];

type PopulationFormat = 'transaction' | 'ndjson';

const BENCH_LABELS: Record<TargetBench, string> = {
  fhirpath: 'FHIRPath',
  fml: 'FML',
  sqlonfhir: 'SQL on FHIR',
};

const deliveryBarStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 12,
  flexWrap: 'wrap',
  background: 'var(--panel)',
  border: '1px solid var(--border)',
  borderRadius: 12,
  padding: '9px 12px',
};

const barDividerStyle: CSSProperties = { width: 1, height: 22, background: 'var(--border2)', flex: 'none' };

const downloadButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  padding: '8px 15px',
  borderRadius: 8,
  background: 'var(--accent)',
  color: 'var(--accent-contrast)',
  fontSize: 12.5,
  fontWeight: 600,
  cursor: 'pointer',
  border: 'none',
  boxShadow: 'var(--accent-shadow)',
};

const resultPreStyle: CSSProperties = {
  margin: 0,
  padding: '12px 14px',
  borderRadius: 8,
  background: 'var(--code)',
  border: '1px solid var(--border)',
  fontFamily: monoFont,
  fontSize: 11,
  lineHeight: 1.55,
  whiteSpace: 'pre-wrap',
  wordBreak: 'break-word',
  maxHeight: 420,
  overflow: 'auto',
};

/** Props for {@link FakesBench}. */
export interface FakesBenchProps {
  /** Set when Fakes was opened from another bench via "⚡ Fakes ↗", to show the return banner. */
  returnTo?: TargetBench | null;
  /** Called when the user dismisses the return banner. */
  onDismissReturn?: () => void;
  /** Called with the target bench and generated payload when the user sends it to another bench. Omitted in Population mode, which has no single-resource payload to send. */
  onSend?: OnSend;
  initialState?: FakesShareState;
  onShareStateChange?: (state: FakesShareState) => void;
}

export function FakesBench({ returnTo, onDismissReturn, onSend, initialState, onShareStateChange }: FakesBenchProps) {
  const stacked = useIsNarrowViewport(720);
  const [mode, setMode] = useState<FakesMode>(initialState?.mode ?? 'resource');
  const [metadata, setMetadata] = useState<FakesMetadata | null>(null);
  const [metadataError, setMetadataError] = useState<string | null>(null);
  const [fhirVersion, setFhirVersion] = useState(initialState?.fhirVersion ?? 'r4');
  const [populationShare, setPopulationShare] = useState(initialState?.population);
  const [scenarioShare, setScenarioShare] = useState(initialState?.scenario);
  const [resourceShare, setResourceShare] = useState(initialState?.resource);

  // FHIRPath evaluates a single resource against an expression — Population/Scenario
  // produce a cohort or bundle, neither of which fits what "⚡ Fakes ↗" from that
  // bench is asking for, so restrict to Resource mode rather than let the user
  // generate something that can't actually be sent back.
  const resourceOnly = returnTo === 'fhirpath';

  useEffect(() => {
    if (resourceOnly) {
      setMode('resource');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resourceOnly]);

  const modeItems = useMemo(
    () =>
      resourceOnly
        ? MODE_ITEMS.map((item) =>
            item.id === 'resource' ? item : { ...item, disabled: true, title: 'FHIRPath needs a single resource' },
          )
        : MODE_ITEMS,
    [resourceOnly],
  );

  useEffect(() => {
    const controller = new AbortController();
    getFakesMetadata(controller.signal)
      .then((meta) => {
        setMetadata(meta);
        if (meta.fhirVersions.length > 0 && !meta.fhirVersions.includes(fhirVersion)) {
          setFhirVersion(meta.fhirVersions[0]);
        }
      })
      .catch((error: Error) => {
        if (error.name !== 'AbortError') {
          setMetadataError(error.message);
        }
      });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    onShareStateChange?.({
      mode,
      fhirVersion,
      population: populationShare,
      scenario: scenarioShare,
      resource: resourceShare,
    });
  }, [fhirVersion, mode, onShareStateChange, populationShare, resourceShare, scenarioShare]);

  const fhirVersionItems = useMemo<PillItem<string>[]>(
    () => (metadata?.fhirVersions ?? []).map((version) => ({ id: version, label: version.toUpperCase() })),
    [metadata?.fhirVersions],
  );

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>Fakes</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>
          Generate realistic synthetic FHIR data — populations, clinical scenarios, and edge-case fuzzing.
        </span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>{metadata ? `ignixa-fakes ${metadata.libraryVersion}` : 'ignixa-fakes'}</span>
      </div>

      {metadataError ? <ErrorBanner message={`Failed to load Fakes metadata: ${metadataError}`} /> : null}

      <div style={deliveryBarStyle}>
        <Pills items={modeItems} activeId={mode} onChange={setMode} />
        {fhirVersionItems.length > 0 ? (
          <>
            <div style={barDividerStyle} />
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <span style={sectionLabelStyle}>FHIR</span>
              <Pills items={fhirVersionItems} activeId={fhirVersion} onChange={setFhirVersion} />
            </div>
          </>
        ) : null}
      </div>

      {returnTo ? (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 12,
            flexWrap: 'wrap',
            padding: '11px 16px',
            borderRadius: 10,
            background: 'var(--chip-vio-bg)',
            border: '1px solid var(--accent-border)',
          }}
        >
          <span style={{ fontSize: 13 }}>
            Generating for the <b style={{ color: 'var(--accent)' }}>{BENCH_LABELS[returnTo]}</b> bench — configure a source below, then send it straight in.
          </span>
          <div style={{ flex: 1 }} />
          <button type="button" onClick={onDismissReturn} style={{ fontSize: 12, fontWeight: 600, color: 'var(--text3)', border: 'none', background: 'transparent', cursor: 'pointer' }}>
            Dismiss
          </button>
        </div>
      ) : null}

      {!metadata ? (
        <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text3)' }}>Loading…</span>
      ) : (
        <>
          {mode === 'population' ? <PopulationPanel metadata={metadata} fhirVersion={fhirVersion} stacked={stacked} initialState={initialState?.population} onShareStateChange={setPopulationShare} /> : null}
          {mode === 'scenario' ? <ScenarioPanel metadata={metadata} fhirVersion={fhirVersion} stacked={stacked} onSend={onSend} initialState={initialState?.scenario} onShareStateChange={setScenarioShare} /> : null}
          {mode === 'resource' ? <ResourcePanel metadata={metadata} fhirVersion={fhirVersion} stacked={stacked} onSend={onSend} initialState={initialState?.resource} onShareStateChange={setResourceShare} /> : null}
        </>
      )}
    </div>
  );
}

function twoColumnStyle(stacked: boolean, minmax: string): CSSProperties {
  return { display: 'grid', gridTemplateColumns: stacked ? '1fr' : `${minmax} 1fr`, gap: 14, alignItems: 'start' };
}

// ---------------------------------------------------------------------------
// Export + CLI helpers
// ---------------------------------------------------------------------------

function downloadBlob(filename: string, content: string, mime: string) {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

function downloadJson(filename: string, data: unknown) {
  downloadBlob(filename, JSON.stringify(data, null, 2), 'application/json');
}

function slug(value: string): string {
  return value.trim().replace(/\s+/g, '-').replace(/[^A-Za-z0-9._-]/g, '') || 'out';
}

/** Quotes a CLI argument value when it contains whitespace, so the rendered hint stays copy-pasteable. */
function cliArg(value: string): string {
  return /\s/.test(value) ? `"${value}"` : value;
}

function populationCli(version: string, source: string, count: number, format: PopulationFormat): string {
  const parts = ['ignixa-fakes', version, 'population', `--from ${cliArg(source)}`, `--count ${count}`];
  if (format === 'ndjson') {
    parts.push('--ndjson');
  }
  parts.push('--out ./out');
  return parts.join(' ');
}

function scenarioCli(version: string, scenarioId: string, resolvedReferences: boolean): string {
  const parts = ['ignixa-fakes', version, 'scenario', scenarioId];
  if (resolvedReferences) {
    parts.push('--resolved-references');
  }
  parts.push('--out ./out');
  return parts.join(' ');
}

function resourceCli(params: {
  version: string;
  resourceType: string;
  observationState?: string;
  firstName?: string;
  familyName?: string;
  city?: string;
  density: string;
  seed: number;
  edgeSelectors: string[];
  includeInvalid: boolean;
}): string {
  const parts = ['ignixa-fakes', params.version, 'resource', params.resourceType];
  if (params.resourceType === 'Observation' && params.observationState) {
    parts.push(params.observationState);
  }
  if (params.firstName) {
    parts.push(`--firstname ${cliArg(params.firstName)}`);
  }
  if (params.familyName) {
    parts.push(`--surname ${cliArg(params.familyName)}`);
  }
  if (params.city) {
    parts.push(`--from ${cliArg(params.city)}`);
  }
  if (params.density && params.density.toLowerCase() !== 'minimal') {
    parts.push(`--density ${params.density.toLowerCase()}`);
  }
  parts.push(`--seed ${params.seed}`);
  if (params.edgeSelectors.length > 0) {
    parts.push(`--edge-cases ${params.edgeSelectors.join(',')}`);
    if (params.includeInvalid) {
      parts.push('--include-invalid');
    }
  }
  parts.push('--out ./out');
  return parts.join(' ');
}

/** A copyable monospace `$ ignixa-fakes …` hint reflecting the current UI selections. Decorative, but syntactically real. */
function CliHint({ command }: { command: string }) {
  const { copied, copy } = useCopyToClipboard(command, COPY_FEEDBACK_DURATION_MS);
  return (
    <button
      type="button"
      onClick={copy}
      title="Copy CLI command"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        maxWidth: '100%',
        minWidth: 0,
        textAlign: 'left',
        fontFamily: monoFont,
        fontSize: 10.5,
        color: 'var(--accent)',
        background: 'var(--accent-soft)',
        border: '1px solid var(--accent-border)',
        borderRadius: 6,
        padding: '5px 10px',
        cursor: 'pointer',
        overflow: 'hidden',
      }}
    >
      <span style={{ flex: 'none', color: 'var(--text4)' }}>{copied ? '✓ copied' : '$'}</span>
      <span style={{ minWidth: 0, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{command}</span>
    </button>
  );
}

/** Download + optional Send-to + CLI-hint row shown under every generated result. */
function ResultActions({ cli, onDownload, onSend }: { cli: string; onDownload: () => void; onSend?: (bench: TargetBench) => void }) {
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center' }}>
      <button type="button" onClick={onDownload} style={downloadButtonStyle}>
        ⬇ Download
      </button>
      {onSend ? <SendBar onSend={onSend} /> : null}
      <div style={{ flex: 1, minWidth: 0 }} />
      <CliHint command={cli} />
    </div>
  );
}

// ---------------------------------------------------------------------------
// Population
// ---------------------------------------------------------------------------

const POPULATION_FORMATS: { id: PopulationFormat; label: string; sub: string }[] = [
  { id: 'transaction', label: 'Transaction bundle', sub: 'single Bundle · type=transaction' },
  { id: 'ndjson', label: 'NDJSON', sub: 'one resource per line · .ndjson' },
];

function PopulationPanel({
  metadata,
  fhirVersion,
  stacked,
  initialState,
  onShareStateChange,
}: {
  metadata: FakesMetadata;
  fhirVersion: string;
  stacked: boolean;
  initialState?: FakesShareState['population'];
  onShareStateChange?: (state: NonNullable<FakesShareState['population']>) => void;
}) {
  const [source, setSource] = useState(
    initialState?.source ?? metadata.populationStates.find((state) => state === 'Washington') ?? metadata.populationStates[0] ?? 'Massachusetts',
  );
  const [count, setCount] = useState(initialState?.count ?? 10);
  const [format, setFormat] = useState<PopulationFormat>(initialState?.format ?? 'transaction');
  const [result, setResult] = useState<PopulationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    onShareStateChange?.({ source, count, format });
  }, [count, format, onShareStateChange, source]);

  const generate = () => {
    setIsLoading(true);
    setError(null);
    generatePopulation({ fhirVersion, source, count })
      .then(setResult)
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
  };

  const download = () => {
    if (!result) {
      return;
    }
    if (format === 'ndjson') {
      const ndjson = result.resources.map((resource) => JSON.stringify(resource)).join('\n');
      downloadBlob(`population-${slug(source)}-${count}.ndjson`, ndjson, 'application/x-ndjson');
      return;
    }
    const bundle = {
      resourceType: 'Bundle',
      type: 'transaction',
      entry: result.resources.map((resource) => ({ resource })),
    };
    downloadJson(`population-${slug(source)}-${count}.json`, bundle);
  };

  return (
    <div style={twoColumnStyle(stacked, 'minmax(360px,38%)')}>
      <Card style={{ minWidth: 0 }}>
        <span style={sectionLabelStyle}>Source · US state</span>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 7 }}>
          {metadata.populationStates.map((state) => (
            <button
              key={state}
              type="button"
              onClick={() => setSource(state)}
              style={{
                font: 'inherit',
                fontSize: 12,
                fontWeight: 600,
                padding: '6px 12px',
                borderRadius: 99,
                cursor: 'pointer',
                background: source === state ? 'var(--chip-vio-bg)' : 'var(--panel)',
                color: source === state ? 'var(--chip-vio-fg)' : 'var(--text3)',
                border: `1px solid ${source === state ? 'var(--accent-border)' : 'var(--border2)'}`,
              }}
            >
              {state}
            </button>
          ))}
        </div>
        <span style={{ fontSize: 11, color: 'var(--text4)' }}>
          Demographics — age, gender, city, area distribution — are drawn from the source's real distribution.
        </span>

        <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
          <span style={{ ...sectionLabelStyle, flex: 1 }}>Patient count</span>
          <span style={{ fontFamily: monoFont, fontSize: 19, fontWeight: 600, color: 'var(--accent)', lineHeight: 1 }}>{count}</span>
          <span style={{ fontSize: 11, color: 'var(--text4)' }}>/ 100 max</span>
        </div>
        <input
          type="range"
          min={1}
          max={100}
          value={count}
          onChange={(event) => setCount(Number(event.target.value))}
          style={{ width: '100%', accentColor: 'var(--accent)' }}
        />

        <span style={sectionLabelStyle}>Output format</span>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {POPULATION_FORMATS.map((option) => {
            const active = format === option.id;
            return (
              <div
                key={option.id}
                onClick={() => setFormat(option.id)}
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 1,
                  padding: '10px 12px',
                  borderRadius: 9,
                  cursor: 'pointer',
                  background: active ? 'var(--chip-vio-bg)' : 'var(--panel)',
                  border: `1px solid ${active ? 'var(--accent-border)' : 'var(--border2)'}`,
                }}
              >
                <span style={{ fontSize: 13, fontWeight: 600, color: active ? 'var(--chip-vio-fg)' : 'var(--text)' }}>{option.label}</span>
                <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text4)' }}>{option.sub}</span>
              </div>
            );
          })}
        </div>

        <button type="button" onClick={generate} disabled={isLoading} style={primaryButtonStyle}>
          {isLoading ? 'Generating…' : '⚡ Generate cohort'}
        </button>
      </Card>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 0 }}>
        {error ? <ErrorBanner message={error} /> : null}
        {result ? (
          <Card>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 10 }}>
              <span style={{ ...sectionLabelStyle, flex: 1 }}>Cohort preview</span>
              <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text2)' }}>
                {result.patients.length} patients · {result.resources.length} resources
              </span>
            </div>
            <SummaryBars title="Resource types" counts={result.summary.byType} />
            <SummaryBars title="Gender" counts={result.summary.byGender} />
            <SummaryBars title="Age bands" counts={result.summary.ageBuckets} />
            <SummaryBars title="Top locations" counts={result.summary.byCity} />

            {result.patients[0] ? (
              <>
                <span style={sectionLabelStyle}>Sample patient · first in cohort</span>
                <HighlightedJsonBlock text={JSON.stringify(result.patients[0], null, 2)} style={{ ...resultPreStyle, maxHeight: 240 }} />
              </>
            ) : null}

            <ResultActions cli={populationCli(fhirVersion, source, count, format)} onDownload={download} />
          </Card>
        ) : (
          <Card style={{ alignItems: 'center', textAlign: 'center', padding: '48px 24px' }}>
            <span style={{ fontSize: 14, fontWeight: 700 }}>No cohort generated yet</span>
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>
              Configure the source, count and output format, then hit Generate cohort.
            </span>
          </Card>
        )}
      </div>
    </div>
  );
}

function SummaryBars({ title, counts }: { title: string; counts: Record<string, number> }) {
  const entries = Object.entries(counts).filter(([, count]) => count > 0);
  const max = Math.max(1, ...entries.map(([, count]) => count));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }}>
      <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--text3)' }}>{title}</span>
      {entries.map(([label, count]) => (
        <div key={label} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span style={{ fontSize: 11, color: 'var(--text2)', width: 90, flex: 'none' }}>{label}</span>
          <div style={{ flex: 1, height: 8, borderRadius: 99, background: 'var(--inset)', overflow: 'hidden' }}>
            <div style={{ height: '100%', width: `${(count / max) * 100}%`, background: 'var(--accent)', borderRadius: 99 }} />
          </div>
          <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)', width: 26, textAlign: 'right', flex: 'none' }}>
            {count}
          </span>
        </div>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Scenario
// ---------------------------------------------------------------------------

const SCENARIO_VIEW_ITEMS: PillItem<'tree' | 'bundle'>[] = [
  { id: 'tree', label: 'Tree' },
  { id: 'bundle', label: 'Bundle' },
];

function ScenarioPanel({
  metadata,
  fhirVersion,
  stacked,
  onSend,
  initialState,
  onShareStateChange,
}: {
  metadata: FakesMetadata;
  fhirVersion: string;
  stacked: boolean;
  onSend?: OnSend;
  initialState?: FakesShareState['scenario'];
  onShareStateChange?: (state: NonNullable<FakesShareState['scenario']>) => void;
}) {
  const [scenarioId, setScenarioId] = useState(initialState?.scenarioId ?? metadata.scenarios[0]?.id ?? '');
  const [paramValues, setParamValues] = useState<Record<string, unknown>>(initialState?.paramValues ?? {});
  const [tag, setTag] = useState(initialState?.tag ?? '');
  const [resolvedReferences, setResolvedReferences] = useState(initialState?.resolvedReferences ?? false);
  const [view, setView] = useState<'tree' | 'bundle'>('tree');
  const [result, setResult] = useState<ScenarioResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('All');

  useEffect(() => {
    onShareStateChange?.({ scenarioId, paramValues, tag, resolvedReferences });
  }, [onShareStateChange, paramValues, resolvedReferences, scenarioId, tag]);

  const scenario = useMemo(() => metadata.scenarios.find((s) => s.id === scenarioId), [metadata.scenarios, scenarioId]);
  const description = describeScenario(scenarioId);

  const scenarioInfos = useMemo(
    () => metadata.scenarios.map((s) => ({ id: s.id, ...describeScenario(s.id) })),
    [metadata.scenarios],
  );

  const categoryCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    scenarioInfos.forEach((s) => {
      counts[s.group] = (counts[s.group] ?? 0) + 1;
    });
    return counts;
  }, [scenarioInfos]);

  const categories = useMemo(() => Object.keys(categoryCounts).sort((a, b) => scenarioGroupRank(a) - scenarioGroupRank(b)), [categoryCounts]);

  const searchLower = search.trim().toLowerCase();
  const filteredScenarios = useMemo(
    () =>
      scenarioInfos.filter(
        (s) =>
          (category === 'All' || s.group === category) &&
          (!searchLower || `${s.label} ${s.blurb} ${s.group}`.toLowerCase().includes(searchLower)),
      ),
    [scenarioInfos, category, searchLower],
  );

  const groupedScenarios = useMemo(() => {
    const groups: Record<string, typeof filteredScenarios> = {};
    filteredScenarios.forEach((s) => {
      (groups[s.group] ??= []).push(s);
    });
    return Object.keys(groups)
      .sort((a, b) => scenarioGroupRank(a) - scenarioGroupRank(b))
      .map((group) => ({ group, items: groups[group] }));
  }, [filteredScenarios]);

  const selectScenario = (id: string) => {
    setScenarioId(id);
    setParamValues({});
  };

  const generate = () => {
    setIsLoading(true);
    setError(null);
    generateScenario({ fhirVersion, scenarioId, parameters: paramValues, tag: tag || undefined, resolvedReferences })
      .then(setResult)
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <Card>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
          <span style={{ ...sectionLabelStyle, flex: 1 }}>Predefined clinical scenarios</span>
          <div style={{ display: 'flex', alignItems: 'center', gap: 7, background: 'var(--inset)', border: '1px solid var(--border2)', borderRadius: 8, padding: '6px 11px', minWidth: 210 }}>
            <span style={{ color: 'var(--text4)', fontSize: 12 }}>⌕</span>
            <input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Search scenarios…"
              style={{ border: 'none', outline: 'none', background: 'transparent', fontSize: 12.5, color: 'var(--text)', width: '100%' }}
            />
          </div>
        </div>

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
          {['All', ...categories].map((name) => {
            const active = category === name;
            const count = name === 'All' ? scenarioInfos.length : categoryCounts[name];
            return (
              <span
                key={name}
                onClick={() => setCategory(name)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 6,
                  fontSize: 11.5,
                  fontWeight: 600,
                  padding: '5px 11px',
                  borderRadius: 99,
                  cursor: 'pointer',
                  background: active ? 'var(--accent)' : 'var(--panel)',
                  color: active ? 'var(--accent-contrast)' : 'var(--text2)',
                  border: `1px solid ${active ? 'var(--accent)' : 'var(--border2)'}`,
                }}
              >
                {name}
                <span style={{ fontFamily: monoFont, fontSize: 9.5, color: active ? 'rgba(255,255,255,.7)' : 'var(--text4)' }}>{count}</span>
              </span>
            );
          })}
        </div>

        {filteredScenarios.length === 0 ? (
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8, padding: '34px 20px', textAlign: 'center' }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: 'var(--text2)' }}>No scenarios match your filter</span>
            <button
              type="button"
              onClick={() => {
                setSearch('');
                setCategory('All');
              }}
              style={{ fontSize: 12, fontWeight: 600, padding: '6px 13px', borderRadius: 7, border: '1px solid var(--border2)', color: 'var(--accent)', background: 'transparent', cursor: 'pointer' }}
            >
              Clear filter
            </button>
          </div>
        ) : category === 'All' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {groupedScenarios.map(({ group, items }) => {
              const [groupBg, groupColor] = scenarioGroupChip(group);
              return (
                <div key={group} style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 9 }}>
                    <span style={{ ...chipStyle(groupBg, groupColor), textTransform: 'uppercase', letterSpacing: '.06em' }}>{group}</span>
                    <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)' }}>{items.length}</span>
                    <div style={{ flex: 1, height: 1, background: 'var(--border)' }} />
                  </div>
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill,minmax(220px,1fr))', gap: 9 }}>
                    {items.map((s) => (
                      <ScenarioCard key={s.id} info={s} active={scenarioId === s.id} onClick={() => selectScenario(s.id)} />
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill,minmax(220px,1fr))', gap: 9 }}>
            {filteredScenarios.map((s) => (
              <ScenarioCard key={s.id} info={s} active={scenarioId === s.id} onClick={() => selectScenario(s.id)} />
            ))}
          </div>
        )}
      </Card>

      <div style={twoColumnStyle(stacked, 'minmax(300px,32%)')}>
        <Card style={{ minWidth: 0 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <span style={{ fontSize: 15, fontWeight: 700, letterSpacing: '-.01em' }}>{description.label}</span>
            <span style={{ fontSize: 11.5, lineHeight: 1.45, color: 'var(--text3)' }}>{description.blurb}</span>
          </div>

          {scenario?.parameters.map((param) => (
            <ScenarioParameterControl
              key={param.name}
              param={param}
              value={paramValues[param.name] ?? param.defaultValue}
              onChange={(value) => setParamValues((current) => ({ ...current, [param.name]: value }))}
            />
          ))}

          <span style={sectionLabelStyle}>Test-isolation tag · optional</span>
          <input value={tag} onChange={(event) => setTag(event.target.value)} placeholder="e.g. test-run-123" style={monoInputStyle} />

          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 1, flex: 1 }}>
              <span style={{ fontSize: 12.5, fontWeight: 600 }}>Resolved references</span>
              <span style={{ fontSize: 10.5, color: 'var(--text4)' }}>batch bundle instead of transaction</span>
            </div>
            <Toggle checked={resolvedReferences} onChange={setResolvedReferences} ariaLabel="Resolved references" />
          </div>

          <button type="button" onClick={generate} disabled={isLoading || !scenarioId} style={primaryButtonStyle}>
            {isLoading ? 'Generating…' : '⚡ Generate scenario'}
          </button>
        </Card>

        <Card style={{ minHeight: 360, minWidth: 0 }}>
          {error ? <ErrorBanner message={error} /> : null}
          {result ? (
            <>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <Pills items={SCENARIO_VIEW_ITEMS} activeId={view} onChange={setView} />
                <div style={{ flex: 1 }} />
                <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>{result.resources.length} resources</span>
              </div>

              {view === 'tree' ? (
                <ScenarioTree resources={result.resources} />
              ) : (
                <HighlightedJsonBlock text={JSON.stringify(result.bundle, null, 2)} style={{ ...resultPreStyle, maxHeight: 460 }} />
              )}

              <ResultActions
                cli={scenarioCli(fhirVersion, scenarioId, resolvedReferences)}
                onDownload={() => downloadJson(`scenario-${slug(scenarioId)}.json`, result.bundle)}
                onSend={
                  onSend
                    ? (targetBench) =>
                        onSend(
                          targetBench,
                          targetBench === 'sqlonfhir' ? result.resources : result.patient ?? result.bundle,
                          `${scenarioId} · ${result.resources.length} resources`,
                        )
                    : undefined
                }
              />
            </>
          ) : (
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>Generate a scenario to see its resources here.</span>
          )}
        </Card>
      </div>
    </div>
  );
}

/** Display order for scenario category chips and group section headers; unlisted groups sort last. */
const SCENARIO_GROUP_ORDER = ['Emergency', 'Preventive', 'Wellness', 'Infection', 'Chronic', 'Respiratory', 'Cardiometabolic', 'Cancer', 'Mental health', 'Surgical', 'Obstetric'];

function scenarioGroupRank(group: string): number {
  const index = SCENARIO_GROUP_ORDER.indexOf(group);
  return index === -1 ? SCENARIO_GROUP_ORDER.length : index;
}

const SCENARIO_GROUP_CHIP_COLORS: Record<string, [string, string]> = {
  Emergency: ['var(--chip-red-bg)', 'var(--fail)'],
  Preventive: ['var(--pass-bg)', 'var(--pass)'],
  Wellness: ['var(--pass-bg)', 'var(--pass)'],
  Infection: ['var(--chip-amb-bg)', 'var(--chip-amb-fg)'],
  Chronic: ['var(--chip-teal-bg)', 'var(--chip-teal-fg)'],
  Respiratory: ['var(--chip-ind-bg)', 'var(--chip-ind-fg)'],
  Cardiometabolic: ['var(--chip-teal-bg)', 'var(--chip-teal-fg)'],
  Cancer: ['var(--chip-vio-bg)', 'var(--chip-vio-fg)'],
  'Mental health': ['var(--chip-pink-bg)', 'var(--chip-pink-fg)'],
  Surgical: ['var(--chip-ind-bg)', 'var(--chip-ind-fg)'],
  Obstetric: ['var(--chip-pink-bg)', 'var(--chip-pink-fg)'],
};

function scenarioGroupChip(group: string): [string, string] {
  return SCENARIO_GROUP_CHIP_COLORS[group] ?? ['var(--chip-gray-bg)', 'var(--chip-gray-fg)'];
}

/** A single scenario card — group chip, name, and blurb — shared by the grouped and flat scenario list layouts. */
function ScenarioCard({
  info,
  active,
  onClick,
}: {
  info: { group: string; label: string; blurb: string };
  active: boolean;
  onClick: () => void;
}) {
  const [groupBg, groupColor] = scenarioGroupChip(info.group);
  return (
    <div
      onClick={onClick}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 6,
        padding: '12px 13px',
        borderRadius: 10,
        cursor: 'pointer',
        background: active ? 'var(--chip-vio-bg)' : 'var(--panel)',
        border: `1px solid ${active ? 'var(--accent-border)' : 'var(--border)'}`,
      }}
    >
      <span style={{ ...chipStyle(groupBg, groupColor), alignSelf: 'flex-start', textTransform: 'uppercase', letterSpacing: '.06em' }}>{info.group}</span>
      <span style={{ fontSize: 13.5, fontWeight: 700, letterSpacing: '-.01em' }}>{info.label}</span>
      <span style={{ fontSize: 11.5, lineHeight: 1.45, color: 'var(--text3)' }}>{info.blurb}</span>
    </div>
  );
}

/** Groups scenario resources by `resourceType` and shows a one-line salient summary per resource. */
function ScenarioTree({ resources }: { resources: Record<string, unknown>[] }) {
  const groups = useMemo(() => {
    const byType = new Map<string, Record<string, unknown>[]>();
    for (const resource of resources) {
      const type = String(resource.resourceType ?? 'Unknown');
      const bucket = byType.get(type);
      if (bucket) {
        bucket.push(resource);
      } else {
        byType.set(type, [resource]);
      }
    }
    return [...byType.entries()];
  }, [resources]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12, maxHeight: 460, overflow: 'auto' }}>
      {groups.map(([type, items]) => (
        <div key={type} style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={chipStyle('var(--chip-vio-bg)', 'var(--chip-vio-fg)')}>{type}</span>
            <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text4)' }}>×{items.length}</span>
            <div style={{ flex: 1, height: 1, background: 'var(--border)' }} />
          </div>
          {items.map((resource, index) => {
            const id = typeof resource.id === 'string' ? resource.id : '';
            return (
              <div
                key={id || index}
                style={{
                  display: 'flex',
                  gap: 10,
                  alignItems: 'baseline',
                  padding: '7px 12px',
                  borderRadius: 8,
                  background: 'var(--code)',
                  border: '1px solid var(--border)',
                }}
              >
                <span style={{ fontSize: 12.5, fontWeight: 600, flex: 1, minWidth: 0, wordBreak: 'break-word' }}>{summarizeResource(resource)}</span>
                {id ? (
                  <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)', flex: 'none', whiteSpace: 'nowrap' }}>#{id}</span>
                ) : null}
              </div>
            );
          })}
        </div>
      ))}
    </div>
  );
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : undefined;
}

function firstOf(value: unknown): Record<string, unknown> | undefined {
  return Array.isArray(value) ? asRecord(value[0]) : undefined;
}

function codeableText(value: unknown): string | undefined {
  const concept = asRecord(value);
  if (!concept) {
    return undefined;
  }
  if (typeof concept.text === 'string') {
    return concept.text;
  }
  const coding = firstOf(concept.coding);
  if (coding && typeof coding.display === 'string') {
    return coding.display;
  }
  if (coding && typeof coding.code === 'string') {
    return coding.code;
  }
  return undefined;
}

function humanName(resource: Record<string, unknown>): string | undefined {
  const name = firstOf(resource.name);
  if (!name) {
    return undefined;
  }
  if (typeof name.text === 'string') {
    return name.text;
  }
  const given = Array.isArray(name.given) ? name.given.filter((part): part is string => typeof part === 'string').join(' ') : '';
  const family = typeof name.family === 'string' ? name.family : '';
  return `${given} ${family}`.trim() || undefined;
}

function observationValue(resource: Record<string, unknown>): string | undefined {
  const quantity = asRecord(resource.valueQuantity);
  if (quantity && quantity.value != null) {
    return `${quantity.value}${typeof quantity.unit === 'string' ? ` ${quantity.unit}` : ''}`;
  }
  const concept = codeableText(resource.valueCodeableConcept);
  if (concept) {
    return concept;
  }
  return typeof resource.valueString === 'string' ? resource.valueString : undefined;
}

/** Best-effort one-line summary of a FHIR resource for the scenario tree view. Falls back to the id. */
function summarizeResource(resource: Record<string, unknown>): string {
  const type = String(resource.resourceType ?? '');
  const id = typeof resource.id === 'string' ? resource.id : '';
  switch (type) {
    case 'Patient': {
      const parts = [humanName(resource), typeof resource.gender === 'string' ? resource.gender : undefined];
      return parts.filter(Boolean).join(' · ') || id || 'Patient';
    }
    case 'Observation': {
      const parts = [codeableText(resource.code), observationValue(resource)];
      return parts.filter(Boolean).join(' = ') || id || 'Observation';
    }
    case 'MedicationRequest':
    case 'MedicationStatement':
    case 'MedicationAdministration':
      return codeableText(resource.medicationCodeableConcept) ?? id ?? type;
    case 'Condition':
    case 'Procedure':
    case 'AllergyIntolerance':
    case 'DiagnosticReport':
    case 'Immunization':
      return codeableText(resource.code) ?? codeableText(resource.vaccineCode) ?? id ?? type;
    case 'Encounter': {
      const cls = asRecord(resource.class);
      const parts = [codeableText(firstOf(resource.type)), cls && typeof cls.code === 'string' ? cls.code : undefined];
      return parts.filter(Boolean).join(' · ') || id || 'Encounter';
    }
    default:
      return id || type || 'resource';
  }
}

function ScenarioParameterControl({
  param,
  value,
  onChange,
}: {
  param: { name: string; type: string; defaultValue: unknown };
  value: unknown;
  onChange: (value: unknown) => void;
}) {
  if (param.name === 'age') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>Age</span>
        <input
          type="range"
          min={0}
          max={95}
          value={Number(value ?? 0)}
          onChange={(event) => onChange(Number(event.target.value))}
          style={{ width: '100%', accentColor: 'var(--accent)' }}
        />
        <span style={{ fontSize: 11, color: 'var(--text3)' }}>{Number(value ?? 0)}</span>
      </div>
    );
  }

  if (param.name === 'gender') {
    // Several scenarios reflect a `gender = null` default, meaning the underlying
    // library picks randomly unless overridden — defaulting the pill display to
    // "Male" in that case would show a selection that isn't actually being sent,
    // so a genuinely-random default gets its own pill rather than a fake pick.
    const isRandom = value == null;
    const genderItems: PillItem<string>[] = isRandom
      ? [
          { id: 'random', label: 'Random' },
          { id: 'male', label: 'Male' },
          { id: 'female', label: 'Female' },
        ]
      : [
          { id: 'male', label: 'Male' },
          { id: 'female', label: 'Female' },
        ];
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>Gender</span>
        <Pills
          items={genderItems}
          activeId={isRandom ? 'random' : String(value)}
          onChange={(id) => onChange(id === 'random' ? null : id)}
        />
      </div>
    );
  }

  if (param.name === 'severity') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>Severity</span>
        <input
          type="range"
          min={1}
          max={3}
          value={Number(value ?? 1)}
          onChange={(event) => onChange(Number(event.target.value))}
          style={{ width: '100%', accentColor: 'var(--accent)' }}
        />
      </div>
    );
  }

  if (param.type === 'Boolean') {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <span style={{ fontSize: 12.5, fontWeight: 600, flex: 1 }}>{param.name}</span>
        <Toggle checked={Boolean(value)} onChange={onChange} ariaLabel={param.name} />
      </div>
    );
  }

  if (param.type === 'Int32' || param.type === 'Decimal') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>{param.name}</span>
        <input value={String(value ?? '')} onChange={(event) => onChange(Number(event.target.value))} style={monoInputStyle} />
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
      <span style={sectionLabelStyle}>{param.name}</span>
      <input value={String(value ?? '')} onChange={(event) => onChange(event.target.value)} style={monoInputStyle} />
    </div>
  );
}

// ---------------------------------------------------------------------------
// Resource
// ---------------------------------------------------------------------------

const RESOURCE_VIEW_ITEMS: PillItem<'resource' | 'manifest'>[] = [
  { id: 'resource', label: 'Resource' },
  { id: 'manifest', label: 'Manifest' },
];

function ResourcePanel({
  metadata,
  fhirVersion,
  stacked,
  onSend,
  initialState,
  onShareStateChange,
}: {
  metadata: FakesMetadata;
  fhirVersion: string;
  stacked: boolean;
  onSend?: OnSend;
  initialState?: FakesShareState['resource'];
  onShareStateChange?: (state: NonNullable<FakesShareState['resource']>) => void;
}) {
  const edgeCaseFamilies = metadata.edgeCaseFamilies;
  const [resourceType, setResourceType] = useState(initialState?.resourceType ?? 'Patient');
  const [pickerOpen, setPickerOpen] = useState(false);
  const [typeFilter, setTypeFilter] = useState('');
  const [density, setDensity] = useState(initialState?.density ?? 'Minimal');
  const [seed, setSeed] = useState(initialState?.seed ?? 42);
  const [randomizeSeed, setRandomizeSeed] = useState(initialState?.randomizeSeed ?? true);
  const [observationState, setObservationState] = useState(initialState?.observationState ?? metadata.observationStates[0] ?? '');
  const [firstName, setFirstName] = useState(initialState?.firstName ?? '');
  const [familyName, setFamilyName] = useState(initialState?.familyName ?? '');
  const [city, setCity] = useState(
    initialState?.city ?? metadata.patientCities.find((cityName) => cityName === 'Seattle') ?? metadata.patientCities[0] ?? '',
  );
  const [edgeCaseOn, setEdgeCaseOn] = useState(initialState?.edgeCaseOn ?? false);
  const [includeInvalid, setIncludeInvalid] = useState(initialState?.includeInvalid ?? false);
  const [selectedCategories, setSelectedCategories] = useState<Record<string, boolean>>(() => initialState?.selectedCategories ?? initialCategorySelection(edgeCaseFamilies));
  const [view, setView] = useState<'resource' | 'manifest'>('resource');
  const [result, setResult] = useState<ResourceResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    onShareStateChange?.({
      resourceType,
      density,
      seed,
      randomizeSeed,
      observationState,
      firstName,
      familyName,
      city,
      edgeCaseOn,
      includeInvalid,
      selectedCategories,
    });
  }, [
    city,
    density,
    edgeCaseOn,
    familyName,
    firstName,
    includeInvalid,
    observationState,
    onShareStateChange,
    randomizeSeed,
    resourceType,
    seed,
    selectedCategories,
  ]);

  const resourceTypes = useMemo(
    () => metadata.resourceTypesByVersion[fhirVersion.toLowerCase()] ?? [],
    [metadata.resourceTypesByVersion, fhirVersion],
  );

  const pillTypes = useMemo(() => {
    const common = COMMON_RESOURCE_TYPES.filter((type) => resourceTypes.includes(type));
    return common.length > 0 ? common : resourceTypes.slice(0, 6);
  }, [resourceTypes]);
  const isCustomType = !pillTypes.includes(resourceType);

  const filteredTypes = useMemo(
    () => resourceTypes.filter((type) => type.toLowerCase().includes(typeFilter.trim().toLowerCase())),
    [resourceTypes, typeFilter],
  );

  const activeSelectors = useMemo(() => {
    if (!edgeCaseOn) {
      return [];
    }
    const selectors: string[] = [];
    for (const family of edgeCaseFamilies) {
      for (const category of family.categories) {
        if (category.intent !== 'PreservesValidity' && !includeInvalid) {
          continue;
        }
        if (selectedCategories[category.id]) {
          selectors.push(category.id);
        }
      }
    }
    return selectors;
  }, [edgeCaseFamilies, edgeCaseOn, includeInvalid, selectedCategories]);

  const generate = () => {
    const activeSeed = randomizeSeed ? Math.floor(Math.random() * 100000) : seed;
    if (randomizeSeed) {
      setSeed(activeSeed);
    }
    setIsLoading(true);
    setError(null);
    generateResource({
      fhirVersion,
      resourceType,
      seed: activeSeed,
      density,
      firstName: firstName || undefined,
      familyName: familyName || undefined,
      city: city || undefined,
      observationState: resourceType === 'Observation' ? observationState : undefined,
      edgeCaseSelectors: activeSelectors.length > 0 ? activeSelectors : undefined,
      includeInvalid,
    })
      .then((generated) => {
        setResult(generated);
        setView(generated.manifest ? view : 'resource');
      })
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
  };

  const selectType = (type: string) => {
    setResourceType(type);
    setPickerOpen(false);
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={twoColumnStyle(stacked, 'minmax(360px,42%)')}>
        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>Resource type</span>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
            <Pills items={pillTypes.map((type) => ({ id: type, label: type }))} activeId={isCustomType ? '' : resourceType} onChange={selectType} />
            {isCustomType ? (
              <span style={{ ...chipStyle('var(--chip-vio-bg)', 'var(--chip-vio-fg)'), fontSize: 12, padding: '6px 11px' }}>{resourceType}</span>
            ) : null}
            <button
              type="button"
              onClick={() => setPickerOpen((open) => !open)}
              title="Browse all FHIR resource types"
              style={{
                fontSize: 12,
                fontWeight: 600,
                padding: '6px 12px',
                borderRadius: 8,
                cursor: 'pointer',
                whiteSpace: 'nowrap',
                border: '1px solid var(--border2)',
                color: 'var(--text2)',
                background: 'var(--panel)',
              }}
            >
              ⋯ More
            </button>
          </div>

          {pickerOpen ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8, padding: '11px 12px', borderRadius: 10, border: '1px solid var(--border2)', background: 'var(--code)' }}>
              <input
                value={typeFilter}
                onChange={(event) => setTypeFilter(event.target.value)}
                placeholder="Filter resource types…"
                style={monoInputStyle}
              />
              <div style={{ maxHeight: 210, overflow: 'auto', display: 'grid', gridTemplateColumns: 'repeat(auto-fill,minmax(150px,1fr))', gap: 2 }}>
                {filteredTypes.map((type) => (
                  <span
                    key={type}
                    onClick={() => selectType(type)}
                    style={{
                      padding: '6px 10px',
                      borderRadius: 6,
                      fontFamily: monoFont,
                      fontSize: 11.5,
                      cursor: 'pointer',
                      background: type === resourceType ? 'var(--chip-vio-bg)' : 'transparent',
                      color: type === resourceType ? 'var(--chip-vio-fg)' : 'var(--text2)',
                      whiteSpace: 'nowrap',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                    }}
                  >
                    {type}
                  </span>
                ))}
              </div>
              <span style={{ fontSize: 10.5, color: 'var(--text4)' }}>
                {filteredTypes.length} types · {resourceType} selected
              </span>
            </div>
          ) : null}

          {resourceType === 'Observation' ? (
            <>
              <span style={sectionLabelStyle}>Clinical state</span>
              <select value={observationState} onChange={(event) => setObservationState(event.target.value)} style={monoInputStyle}>
                {metadata.observationStates.map((state) => (
                  <option key={state} value={state}>
                    {state}
                  </option>
                ))}
              </select>
            </>
          ) : null}

          {resourceType === 'Patient' ? (
            <>
              <span style={sectionLabelStyle}>Demographics · optional overrides</span>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <input value={firstName} onChange={(event) => setFirstName(event.target.value)} placeholder="First name" style={{ ...monoInputStyle, flex: 1, minWidth: 110 }} />
                <input value={familyName} onChange={(event) => setFamilyName(event.target.value)} placeholder="Surname" style={{ ...monoInputStyle, flex: 1, minWidth: 110 }} />
                <select value={city} onChange={(event) => setCity(event.target.value)} title="From city — samples realistic gender, age, zip, and area code for this city" style={{ ...monoInputStyle, flex: 1, minWidth: 110 }}>
                  {metadata.patientCities.map((cityName) => (
                    <option key={cityName} value={cityName}>
                      {cityName}
                    </option>
                  ))}
                </select>
              </div>
            </>
          ) : null}

          <span style={sectionLabelStyle}>Generation density</span>
          <Pills items={DENSITY_ITEMS} activeId={density} onChange={setDensity} />

          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Seed</span>
            <span style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 11, color: 'var(--text3)' }}>
              Randomize
              <Toggle checked={randomizeSeed} onChange={setRandomizeSeed} ariaLabel="Randomize seed on every generate" />
            </span>
            <input
              value={String(seed)}
              onChange={(event) => setSeed(Number(event.target.value) || 0)}
              disabled={randomizeSeed}
              style={{ ...monoInputStyle, width: 110, opacity: randomizeSeed ? 0.6 : 1 }}
            />
            <button
              type="button"
              onClick={() => setSeed(Math.floor(Math.random() * 100000))}
              disabled={randomizeSeed}
              style={{ ...monoInputStyle, cursor: randomizeSeed ? 'default' : 'pointer', opacity: randomizeSeed ? 0.6 : 1 }}
              title="Randomize seed"
            >
              ⟳
            </button>
          </div>
          <span style={{ fontSize: 10.5, color: 'var(--text4)' }}>
            {randomizeSeed ? 'A fresh seed is rolled on every generate — uncheck to reuse a fixed seed.' : 'Reusing a fixed seed reproduces the same output on every generate.'}
          </span>
          {resourceType === 'Observation' && observationState ? (
            <span style={{ fontSize: 10.5, color: 'var(--text4)' }}>
              Seed has no effect for Observation clinical states — this path isn't seed-controlled in the underlying library.
            </span>
          ) : null}

          <button type="button" onClick={generate} disabled={isLoading} style={primaryButtonStyle}>
            {isLoading ? 'Generating…' : '⚡ Generate resource'}
          </button>
        </Card>

        <Card style={{ minHeight: 360, minWidth: 0 }}>
          {error ? <ErrorBanner message={error} /> : null}
          {result ? (
            <>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                {result.manifest ? <Pills items={RESOURCE_VIEW_ITEMS} activeId={view} onChange={setView} /> : null}
                <div style={{ flex: 1 }} />
                <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>
                  {result.manifest ? `${result.manifest.mutations.length} mutations` : 'no edge cases'}
                </span>
              </div>

              {view === 'manifest' && result.manifest ? (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  <span style={{ fontSize: 11, color: 'var(--text4)' }}>
                    Replay record — same seed + config reproduces this exact output. Bound codes, systems, references and ids are never mutated.
                  </span>
                  {result.manifest.mutations.map((mutation, index) => (
                    <div key={index} style={{ display: 'flex', flexDirection: 'column', gap: 5, padding: '10px 12px', borderRadius: 8, background: 'var(--code)', border: '1px solid var(--border)' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                        <span style={chipStyle('var(--chip-vio-bg)', 'var(--chip-vio-fg)')}>{mutation.category}</span>
                        <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text2)' }}>{mutation.path}</span>
                      </div>
                      <div style={{ display: 'flex', gap: 8, alignItems: 'baseline', flexWrap: 'wrap' }}>
                        <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--fail)', textDecoration: 'line-through', opacity: 0.75 }}>{mutation.before}</span>
                        <span style={{ color: 'var(--text4)', fontSize: 11 }}>→</span>
                        <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--pass)', wordBreak: 'break-all' }}>{mutation.after}</span>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <HighlightedJsonBlock text={JSON.stringify(result.resource, null, 2)} style={{ ...resultPreStyle, maxHeight: 460 }} />
              )}

              <ResultActions
                cli={resourceCli({
                  version: fhirVersion,
                  resourceType,
                  observationState,
                  firstName: firstName || undefined,
                  familyName: familyName || undefined,
                  city: city || undefined,
                  density,
                  seed,
                  edgeSelectors: activeSelectors,
                  includeInvalid,
                })}
                onDownload={() => downloadJson(`${slug(resourceType)}.json`, result.resource)}
                onSend={onSend ? (targetBench) => onSend(targetBench, targetBench === 'sqlonfhir' ? [result.resource] : result.resource, `${resourceType} · edge-cased`) : undefined}
              />
            </>
          ) : (
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>Generate a resource to see it here.</span>
          )}
        </Card>
      </div>

      <Card>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2, flex: 1, minWidth: 220 }}>
            <span style={{ fontSize: 14, fontWeight: 700, letterSpacing: '-.01em' }}>Edge-case fuzzing</span>
            <span style={{ fontSize: 11.5, color: 'var(--text3)' }}>
              Valid-but-hostile values that stress parsers, validators, and renderers. One strategy per eligible leaf.
            </span>
          </div>
          <Toggle checked={edgeCaseOn} onChange={setEdgeCaseOn} ariaLabel="Enable edge-case fuzzing" />
        </div>

        {edgeCaseOn ? (
          <>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(300px,1fr))', gap: 12 }}>
              {edgeCaseFamilies.map((family) => (
                <EdgeCaseFamilyCard
                  key={family.family}
                  family={family}
                  includeInvalid={includeInvalid}
                  selectedCategories={selectedCategories}
                  onToggleCategory={(categoryId) =>
                    setSelectedCategories((current) => ({ ...current, [categoryId]: !current[categoryId] }))
                  }
                  onToggleFamily={() =>
                    setSelectedCategories((current) => toggleFamilySelection(current, family, includeInvalid))
                  }
                />
              ))}
            </div>

            <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: '10px 13px', borderRadius: 9, background: 'var(--fail-bg)', border: '1px solid var(--fail-border)' }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 1, flex: 1 }}>
                <span style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--fail)' }}>Include non-validity-preserving strategies</span>
                <span style={{ fontSize: 10.5, color: 'var(--text3)' }}>
                  Enables MayViolate and AlwaysInvalid categories for negative testing (--include-invalid).
                </span>
              </div>
              <Toggle checked={includeInvalid} onChange={setIncludeInvalid} ariaLabel="Include non-validity-preserving strategies" />
            </div>
          </>
        ) : null}
      </Card>
    </div>
  );
}

function initialCategorySelection(families: EdgeCaseFamilyMetadata[]): Record<string, boolean> {
  const selection: Record<string, boolean> = {};
  for (const family of families) {
    for (const category of family.categories) {
      selection[category.id] = category.intent === 'PreservesValidity';
    }
  }
  return selection;
}

/** Toggles every currently-visible category in a family on or off together. */
function toggleFamilySelection(
  current: Record<string, boolean>,
  family: EdgeCaseFamilyMetadata,
  includeInvalid: boolean,
): Record<string, boolean> {
  const visible = family.categories.filter((category) => category.intent === 'PreservesValidity' || includeInvalid);
  const allOn = visible.length > 0 && visible.every((category) => current[category.id]);
  const next = { ...current };
  for (const category of visible) {
    next[category.id] = !allOn;
  }
  return next;
}

function intentBadge(intent: string): { label: string; bg: string; fg: string } {
  switch (intent) {
    case 'PreservesValidity':
      return { label: 'valid', bg: 'var(--chip-teal-bg)', fg: 'var(--chip-teal-fg)' };
    case 'MayViolate':
      return { label: 'may violate', bg: 'var(--chip-amb-bg)', fg: 'var(--chip-amb-fg)' };
    case 'AlwaysInvalid':
      return { label: 'invalid', bg: 'var(--chip-red-bg)', fg: 'var(--fail)' };
    default:
      return { label: intent, bg: 'var(--chip-gray-bg)', fg: 'var(--chip-gray2-fg)' };
  }
}

function EdgeCaseFamilyCard({
  family,
  includeInvalid,
  selectedCategories,
  onToggleCategory,
  onToggleFamily,
}: {
  family: EdgeCaseFamilyMetadata;
  includeInvalid: boolean;
  selectedCategories: Record<string, boolean>;
  onToggleCategory: (categoryId: string) => void;
  onToggleFamily: () => void;
}) {
  const visible = family.categories.filter((category) => category.intent === 'PreservesValidity' || includeInvalid);
  const anySelected = visible.some((category) => selectedCategories[category.id]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8, padding: '12px 13px', borderRadius: 10, background: 'var(--code)', border: '1px solid var(--border)' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <span
          onClick={onToggleFamily}
          style={{
            fontFamily: monoFont,
            fontSize: 11,
            fontWeight: 600,
            padding: '4px 11px',
            borderRadius: 99,
            cursor: 'pointer',
            background: anySelected ? 'var(--chip-vio-bg)' : 'var(--inset)',
            color: anySelected ? 'var(--chip-vio-fg)' : 'var(--text3)',
          }}
        >
          {family.family}
        </span>
        <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)' }}>{visible.length} strategies</span>
      </div>
      {visible.map((category) => {
        const selected = Boolean(selectedCategories[category.id]);
        const badge = intentBadge(category.intent);
        return (
          <div
            key={category.id}
            onClick={() => onToggleCategory(category.id)}
            style={{ display: 'flex', gap: 9, alignItems: 'flex-start', cursor: 'pointer', padding: '2px 0' }}
          >
            <span
              style={{
                width: 15,
                height: 15,
                borderRadius: 4,
                border: `1.5px solid ${selected ? 'var(--accent)' : 'var(--border2)'}`,
                background: selected ? 'var(--accent)' : 'transparent',
                flex: 'none',
                marginTop: 1,
                display: 'grid',
                placeItems: 'center',
                color: '#fff',
                fontSize: 10,
              }}
            >
              {selected ? '✓' : ''}
            </span>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 1, flex: 1, minWidth: 0 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 7, flexWrap: 'wrap' }}>
                <span style={{ fontFamily: monoFont, fontSize: 11.5, fontWeight: 600 }}>{category.id}</span>
                <span style={chipStyle(badge.bg, badge.fg)}>{badge.label}</span>
              </div>
              <span style={{ fontSize: 10.5, lineHeight: 1.4, color: 'var(--text3)' }}>{describeEdgeCase(category.id)}</span>
            </div>
          </div>
        );
      })}
    </div>
  );
}

function SendBar({ onSend }: { onSend: (bench: TargetBench) => void }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      <span style={{ fontSize: 10.5, color: 'var(--text4)', textTransform: 'uppercase', letterSpacing: '.1em' }}>Send to</span>
      <button type="button" onClick={() => onSend('fhirpath')} style={{ ...monoInputStyle, cursor: 'pointer' }}>
        FHIRPath
      </button>
      <button type="button" onClick={() => onSend('fml')} style={{ ...monoInputStyle, cursor: 'pointer' }}>
        FML
      </button>
      <button type="button" onClick={() => onSend('sqlonfhir')} style={{ ...monoInputStyle, cursor: 'pointer' }}>
        SQL on FHIR
      </button>
    </div>
  );
}
