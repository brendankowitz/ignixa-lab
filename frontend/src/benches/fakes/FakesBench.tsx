import { useEffect, useMemo, useState, type CSSProperties } from 'react';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoInputStyle, primaryButtonStyle, sectionLabelStyle } from '../components/styles';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { describeEdgeCase } from './edgeCaseDescriptions';
import { generatePopulation, generateResource, generateScenario, getFakesMetadata } from './fakesApi';
import type { FakesMetadata, PopulationResult, ResourceResult, ScenarioResult } from './fakesTypes';

type FakesMode = 'population' | 'scenario' | 'resource';

const MODE_ITEMS: PillItem<FakesMode>[] = [
  { id: 'population', label: 'Population' },
  { id: 'scenario', label: 'Scenario' },
  { id: 'resource', label: 'Resource' },
];

const DENSITY_ITEMS: PillItem<string>[] = [
  { id: 'Minimal', label: 'Minimal' },
  { id: 'Maximum', label: 'Maximum' },
];

const BENCH_LABELS: Record<'fhirpath' | 'fml' | 'sqlonfhir', string> = {
  fhirpath: 'FHIRPath',
  fml: 'FML',
  sqlonfhir: 'SQL on FHIR',
};

/** Props for {@link FakesBench}. */
export interface FakesBenchProps {
  /** Set when Fakes was opened from another bench via "⚡ Fakes ↗", to show the return banner. */
  returnTo?: 'fhirpath' | 'fml' | 'sqlonfhir' | null;
  /** Called when the user dismisses the return banner. */
  onDismissReturn?: () => void;
  /** Called with the target bench and generated payload when the user sends it to another bench. Omitted in Population mode, which has no single-resource payload to send. */
  onSend?: (targetBench: 'fhirpath' | 'fml' | 'sqlonfhir', payload: Record<string, unknown> | Record<string, unknown>[], label: string) => void;
}

export function FakesBench({ returnTo, onDismissReturn, onSend }: FakesBenchProps) {
  const stacked = useIsNarrowViewport(720);
  const [mode, setMode] = useState<FakesMode>('scenario');
  const [metadata, setMetadata] = useState<FakesMetadata | null>(null);
  const [metadataError, setMetadataError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getFakesMetadata(controller.signal)
      .then(setMetadata)
      .catch((error: Error) => {
        if (error.name !== 'AbortError') {
          setMetadataError(error.message);
        }
      });
    return () => controller.abort();
  }, []);

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>Fakes</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>
          Generate realistic synthetic FHIR data — populations, clinical scenarios, and edge-case fuzzing.
        </span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>ignixa-fakes 0.5</span>
      </div>

      {metadataError ? <ErrorBanner message={`Failed to load Fakes metadata: ${metadataError}`} /> : null}

      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <Pills items={MODE_ITEMS} activeId={mode} onChange={setMode} />
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
          {mode === 'population' ? <PopulationPanel metadata={metadata} stacked={stacked} /> : null}
          {mode === 'scenario' ? <ScenarioPanel metadata={metadata} stacked={stacked} onSend={onSend} /> : null}
          {mode === 'resource' ? <ResourcePanel metadata={metadata} stacked={stacked} onSend={onSend} /> : null}
        </>
      )}
    </div>
  );
}

function twoColumnStyle(stacked: boolean, minmax: string): CSSProperties {
  return { display: 'grid', gridTemplateColumns: stacked ? '1fr' : `${minmax} 1fr`, gap: 14, alignItems: 'start' };
}

function PopulationPanel({ metadata, stacked }: { metadata: FakesMetadata; stacked: boolean }) {
  const [source, setSource] = useState(metadata.populationStates[0] ?? 'Massachusetts');
  const [count, setCount] = useState(10);
  const [result, setResult] = useState<PopulationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const generate = () => {
    setIsLoading(true);
    setError(null);
    generatePopulation({ fhirVersion: 'r4', source, count })
      .then(setResult)
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
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

        <span style={sectionLabelStyle}>Patient count</span>
        <input
          type="range"
          min={1}
          max={100}
          value={count}
          onChange={(event) => setCount(Number(event.target.value))}
          style={{ width: '100%', accentColor: 'var(--accent)' }}
        />
        <span style={{ fontSize: 11, color: 'var(--text4)' }}>{count} patients (capped at 100 in this release)</span>

        <button type="button" onClick={generate} disabled={isLoading} style={primaryButtonStyle}>
          {isLoading ? 'Generating…' : '⚡ Generate cohort'}
        </button>
      </Card>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 0 }}>
        {error ? <ErrorBanner message={error} /> : null}
        {result ? (
          <Card>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 10 }}>
              <span style={sectionLabelStyle}>Cohort preview</span>
              <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text2)' }}>
                {result.patients.length} patients · {result.resources.length} resources
              </span>
            </div>
            <SummaryBars title="Resource types" counts={result.summary.byType} />
            <SummaryBars title="Gender" counts={result.summary.byGender} />
            <SummaryBars title="Age bands" counts={result.summary.ageBuckets} />
            <SummaryBars title="Top locations" counts={result.summary.byCity} />
          </Card>
        ) : (
          <Card style={{ alignItems: 'center', textAlign: 'center', padding: '48px 24px' }}>
            <span style={{ fontSize: 14, fontWeight: 700 }}>No cohort generated yet</span>
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>
              Configure the source, count, then hit Generate cohort.
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

function ScenarioPanel({
  metadata,
  stacked,
  onSend,
}: {
  metadata: FakesMetadata;
  stacked: boolean;
  onSend?: (targetBench: 'fhirpath' | 'fml' | 'sqlonfhir', payload: Record<string, unknown> | Record<string, unknown>[], label: string) => void;
}) {
  const [scenarioId, setScenarioId] = useState(metadata.scenarios[0]?.id ?? '');
  const [paramValues, setParamValues] = useState<Record<string, unknown>>({});
  const [tag, setTag] = useState('');
  const [resolvedReferences, setResolvedReferences] = useState(false);
  const [result, setResult] = useState<ScenarioResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const scenario = useMemo(() => metadata.scenarios.find((s) => s.id === scenarioId), [metadata.scenarios, scenarioId]);

  const generate = () => {
    setIsLoading(true);
    setError(null);
    generateScenario({ fhirVersion: 'r4', scenarioId, parameters: paramValues, tag: tag || undefined, resolvedReferences })
      .then(setResult)
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <Card>
        <span style={sectionLabelStyle}>Predefined clinical scenarios</span>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill,minmax(200px,1fr))', gap: 9 }}>
          {metadata.scenarios.map((s) => (
            <div
              key={s.id}
              onClick={() => {
                setScenarioId(s.id);
                setParamValues({});
              }}
              style={{
                padding: '12px 13px',
                borderRadius: 10,
                cursor: 'pointer',
                background: scenarioId === s.id ? 'var(--chip-vio-bg)' : 'var(--panel)',
                border: `1px solid ${scenarioId === s.id ? 'var(--accent-border)' : 'var(--border)'}`,
                fontSize: 13.5,
                fontWeight: 700,
              }}
            >
              {s.id}
            </div>
          ))}
        </div>
      </Card>

      <div style={twoColumnStyle(stacked, 'minmax(300px,32%)')}>
        <Card style={{ minWidth: 0 }}>
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
            <span style={{ fontSize: 12.5, fontWeight: 600, flex: 1 }}>Resolved references</span>
            <input type="checkbox" checked={resolvedReferences} onChange={(event) => setResolvedReferences(event.target.checked)} />
          </div>

          <button type="button" onClick={generate} disabled={isLoading || !scenarioId} style={primaryButtonStyle}>
            {isLoading ? 'Generating…' : '⚡ Generate scenario'}
          </button>
        </Card>

        <Card style={{ minHeight: 360, minWidth: 0 }}>
          {error ? <ErrorBanner message={error} /> : null}
          {result ? (
            <>
              <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>
                {result.resources.length} resources
              </span>
              <pre
                style={{
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
                }}
              >
                {JSON.stringify(result.bundle, null, 2)}
              </pre>
              {onSend ? (
                <SendBar
                  onSend={(targetBench) =>
                    onSend(
                      targetBench,
                      { single: result.patient ?? result.bundle, array: result.resources }[targetBench === 'sqlonfhir' ? 'array' : 'single'],
                      `${scenarioId} · ${result.resources.length} resources`,
                    )
                  }
                />
              ) : null}
            </>
          ) : (
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>Generate a scenario to see its resources here.</span>
          )}
        </Card>
      </div>
    </div>
  );
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
    const genderItems: PillItem<string>[] = [
      { id: 'male', label: 'Male' },
      { id: 'female', label: 'Female' },
    ];
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>Gender</span>
        <Pills items={genderItems} activeId={String(value ?? 'male')} onChange={onChange} />
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
        <input type="checkbox" checked={Boolean(value)} onChange={(event) => onChange(event.target.checked)} />
      </div>
    );
  }

  if (param.type === 'Int32' || param.type === 'Decimal') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={sectionLabelStyle}>{param.name}</span>
        <input
          value={String(value ?? '')}
          onChange={(event) => onChange(Number(event.target.value))}
          style={monoInputStyle}
        />
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

function ResourcePanel({
  metadata,
  stacked,
  onSend,
}: {
  metadata: FakesMetadata;
  stacked: boolean;
  onSend?: (targetBench: 'fhirpath' | 'fml' | 'sqlonfhir', payload: Record<string, unknown> | Record<string, unknown>[], label: string) => void;
}) {
  const [resourceType, setResourceType] = useState('Patient');
  const [density, setDensity] = useState('Minimal');
  const [seed, setSeed] = useState(42);
  const [observationState, setObservationState] = useState(metadata.observationStates[0] ?? '');
  const [firstName, setFirstName] = useState('');
  const [familyName, setFamilyName] = useState('');
  const [edgeCaseOn, setEdgeCaseOn] = useState(true);
  const [includeInvalid, setIncludeInvalid] = useState(false);
  const [selectedFamilies, setSelectedFamilies] = useState<Record<string, boolean>>({ Unicode: true, Temporal: true });
  const [result, setResult] = useState<ResourceResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const generate = () => {
    setIsLoading(true);
    setError(null);
    const selectors = edgeCaseOn ? Object.keys(selectedFamilies).filter((family) => selectedFamilies[family]) : undefined;
    generateResource({
      fhirVersion: 'r4',
      resourceType,
      seed,
      density,
      firstName: firstName || undefined,
      familyName: familyName || undefined,
      observationState: resourceType === 'Observation' ? observationState : undefined,
      edgeCaseSelectors: selectors,
      includeInvalid,
    })
      .then(setResult)
      .catch((err: Error) => setError(err.message))
      .finally(() => setIsLoading(false));
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={twoColumnStyle(stacked, 'minmax(360px,42%)')}>
        <Card style={{ minWidth: 0 }}>
          <span style={sectionLabelStyle}>Resource type</span>
          <select value={resourceType} onChange={(event) => setResourceType(event.target.value)} style={monoInputStyle}>
            {metadata.resourceTypes.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </select>

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
            <div style={{ display: 'flex', gap: 8 }}>
              <input value={firstName} onChange={(event) => setFirstName(event.target.value)} placeholder="First name" style={{ ...monoInputStyle, flex: 1 }} />
              <input value={familyName} onChange={(event) => setFamilyName(event.target.value)} placeholder="Surname" style={{ ...monoInputStyle, flex: 1 }} />
            </div>
          ) : null}

          <span style={sectionLabelStyle}>Generation density</span>
          <Pills items={DENSITY_ITEMS} activeId={density} onChange={setDensity} />

          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Seed</span>
            <input
              value={String(seed)}
              onChange={(event) => setSeed(Number(event.target.value) || 0)}
              style={{ ...monoInputStyle, width: 110 }}
            />
            <button type="button" onClick={() => setSeed(Math.floor(Math.random() * 100000))} style={{ ...monoInputStyle, cursor: 'pointer' }}>
              ⟳
            </button>
          </div>
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
              <pre
                style={{
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
                }}
              >
                {JSON.stringify(result.resource, null, 2)}
              </pre>
              {result.manifest ? (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  <span style={{ fontSize: 11, color: 'var(--text4)' }}>{result.manifest.mutations.length} mutations applied</span>
                  {result.manifest.mutations.map((mutation, index) => (
                    <div key={index} style={{ display: 'flex', flexDirection: 'column', gap: 3, padding: '8px 10px', borderRadius: 8, background: 'var(--code)', border: '1px solid var(--border)' }}>
                      <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text2)' }}>{mutation.path}</span>
                      <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--fail)' }}>{mutation.before}</span>
                      <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--pass)' }}>{mutation.after}</span>
                    </div>
                  ))}
                </div>
              ) : null}
              {onSend ? (
                <SendBar onSend={(targetBench) => onSend(targetBench, targetBench === 'sqlonfhir' ? [result.resource] : result.resource, `${resourceType} · edge-cased`)} />
              ) : null}
            </>
          ) : (
            <span style={{ fontSize: 12, color: 'var(--text3)' }}>Generate a resource to see it here.</span>
          )}
        </Card>
      </div>

      <Card>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <span style={{ fontSize: 14, fontWeight: 700, flex: 1 }}>Edge-case fuzzing</span>
          <input type="checkbox" checked={edgeCaseOn} onChange={(event) => setEdgeCaseOn(event.target.checked)} />
        </div>
        {edgeCaseOn ? (
          <>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(280px,1fr))', gap: 12 }}>
              {metadata.edgeCaseFamilies.map((family) => (
                <div key={family.family} style={{ padding: '12px 13px', borderRadius: 10, background: 'var(--code)', border: '1px solid var(--border)' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span
                      onClick={() => setSelectedFamilies((current) => ({ ...current, [family.family]: !current[family.family] }))}
                      style={{
                        fontFamily: monoFont,
                        fontSize: 11,
                        fontWeight: 600,
                        padding: '4px 11px',
                        borderRadius: 99,
                        cursor: 'pointer',
                        background: selectedFamilies[family.family] ? 'var(--chip-vio-bg)' : 'var(--inset)',
                      }}
                    >
                      {family.family}
                    </span>
                  </div>
                  {family.categories.map((category) => (
                    <div key={category.id} style={{ padding: '4px 0' }}>
                      <span style={{ fontFamily: monoFont, fontSize: 11.5, fontWeight: 600 }}>{category.id}</span>
                      <div style={{ fontSize: 10.5, color: 'var(--text3)' }}>{describeEdgeCase(category.id)}</div>
                    </div>
                  ))}
                </div>
              ))}
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ fontSize: 12.5, fontWeight: 600, flex: 1 }}>Include non-validity-preserving strategies</span>
              <input type="checkbox" checked={includeInvalid} onChange={(event) => setIncludeInvalid(event.target.checked)} />
            </div>
          </>
        ) : null}
      </Card>
    </div>
  );
}

function SendBar({ onSend }: { onSend: (bench: 'fhirpath' | 'fml' | 'sqlonfhir') => void }) {
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
