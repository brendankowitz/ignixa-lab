import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { HighlightedTextarea } from '../components/HighlightedTextarea';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoTextareaStyle, primaryButtonStyle, sectionLabelStyle } from '../components/styles';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { getErrorMessage } from '../shared/errorMessage';
import { diffLines } from './diffLines';
import { buildFmlRequest, parseFmlResponse, runFml, type FmlEvalResult } from './fmlApi';
import { DEFAULT_EXPECTED_TEXT, DEFAULT_MAP_TEXT, DEFAULT_SOURCE_TEXT } from './fmlFixtures';
import { highlightFml } from './fmlHighlight';

type FmlTab = 'output' | 'diff' | 'trace';

const TAB_ITEMS: PillItem<FmlTab>[] = [
  { id: 'output', label: 'Output' },
  { id: 'diff', label: 'Diff vs expected' },
  { id: 'trace', label: 'Trace' },
];

export interface FmlBenchProps {
  onOpenFakes?: () => void;
  fakesSeed?: { text: string } | null;
  onSeedConsumed?: () => void;
}

const EMPTY_RESULT: FmlEvalResult = { error: null, evaluator: '', output: null, trace: [], outcomeIssues: [] };

/** Reads the `'MapName'` string out of a map's declaration line (`map 'url' = 'MapName'`), for display next to the editor. Returns '' if the map has no declaration line yet. FML string literals are single-quoted (the tokenizer treats double-quoted text as a DelimitedIdentifier, not a string literal), so this must match single quotes to work with any map the real backend can actually parse. */
function extractMapName(mapText: string): string {
  const match = mapText.match(/map\s+'[^']*'\s*=\s*'([^']+)'/);
  return match ? match[1] : '';
}

export function FmlBench({ onOpenFakes, fakesSeed, onSeedConsumed }: FmlBenchProps) {
  const stacked = useIsNarrowViewport(720);
  const twoColumnStyle: CSSProperties = {
    display: 'grid',
    gridTemplateColumns: stacked ? '1fr' : 'minmax(420px,52%) 1fr',
    gap: 14,
    alignItems: 'start',
  };

  const [mapText, setMapText] = useState(DEFAULT_MAP_TEXT);
  const [sourceText, setSourceText] = useState(DEFAULT_SOURCE_TEXT);
  const [expectedText, setExpectedText] = useState(DEFAULT_EXPECTED_TEXT);
  const [tab, setTab] = useState<FmlTab>('output');
  const [result, setResult] = useState<FmlEvalResult>(EMPTY_RESULT);
  const [isLoading, setIsLoading] = useState(false);
  const abortControllerRef = useRef<AbortController | null>(null);

  // Abort any in-flight request if the bench unmounts mid-run.
  useEffect(() => () => abortControllerRef.current?.abort(), []);

  useEffect(() => {
    if (fakesSeed) {
      setSourceText(fakesSeed.text);
      onSeedConsumed?.();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fakesSeed]);

  const highlightedLines = useMemo(() => highlightFml(mapText), [mapText]);
  const mapName = useMemo(() => extractMapName(mapText), [mapText]);
  const diffRows = useMemo(() => (result.output ? diffLines(result.output, expectedText) : []), [result.output, expectedText]);

  const runMap = () => {
    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;
    setIsLoading(true);

    let body;
    try {
      body = buildFmlRequest({ mapText, resourceText: sourceText });
    } catch (error) {
      setIsLoading(false);
      setResult({ ...EMPTY_RESULT, error: `Source JSON — ${getErrorMessage(error)}` });
      return;
    }

    runFml(body, controller.signal)
      .then((response) => setResult(parseFmlResponse(response)))
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return;
        }
        setResult({ ...EMPTY_RESULT, error: getErrorMessage(error) });
      })
      .finally(() => {
        if (abortControllerRef.current === controller) {
          setIsLoading(false);
        }
      });
  };

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>FHIR Mapping Language</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>Author a StructureMap in FML and debug it rule by rule.</span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>{result.evaluator || (isLoading ? 'transforming…' : 'ignixa-lab')}</span>
        <button type="button" onClick={runMap} style={primaryButtonStyle} disabled={isLoading}>
          ▶ Run map
        </button>
      </div>

      <div style={twoColumnStyle}>
        <Card style={{ minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Map source · .fml</span>
            <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>{mapName}</span>
          </div>
          <HighlightedTextarea value={mapText} onChange={setMapText} lines={highlightedLines} style={{ height: 300 }} />
        </Card>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 0 }}>
          <Card>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ ...sectionLabelStyle, flex: 1 }}>Source resource</span>
              {onOpenFakes ? (
                <button
                  type="button"
                  onClick={onOpenFakes}
                  title="Generate a source resource with Fakes"
                  style={{
                    fontSize: 11,
                    fontWeight: 600,
                    padding: '4px 11px',
                    borderRadius: 99,
                    cursor: 'pointer',
                    background: 'var(--chip-vio-bg)',
                    color: 'var(--accent)',
                    border: '1px solid var(--accent-border)',
                  }}
                >
                  ⚡ Fakes ↗
                </button>
              ) : null}
            </div>
            <textarea
              value={sourceText}
              onChange={(event) => setSourceText(event.target.value)}
              spellCheck={false}
              style={{ ...monoTextareaStyle, height: 160, fontSize: 11.5 }}
            />
          </Card>

          <Card>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <Pills items={TAB_ITEMS} activeId={tab} onChange={setTab} />
              <div style={{ flex: 1 }} />
              <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>
                {result.output ? `${result.trace.length} trace ${result.trace.length === 1 ? 'entry' : 'entries'}` : ''}
              </span>
            </div>

            {result.error ? <ErrorBanner message={result.error} /> : null}
            {!result.error && result.outcomeIssues.length > 0 ? (
              <ErrorBanner message={`${result.outcomeIssues.length} rule error(s): ${result.outcomeIssues.join('; ')}`} />
            ) : null}

            {!result.error && tab === 'output' ? (
              <pre
                style={{
                  margin: 0,
                  padding: '12px 14px',
                  borderRadius: 8,
                  background: 'var(--code)',
                  border: '1px solid var(--border)',
                  fontFamily: monoFont,
                  fontSize: 11.5,
                  lineHeight: 1.6,
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                  color: 'var(--text)',
                  maxHeight: 420,
                  overflow: 'auto',
                }}
              >
                {result.output ?? '—'}
              </pre>
            ) : null}

            {!result.error && tab === 'diff' ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                <textarea
                  value={expectedText}
                  onChange={(event) => setExpectedText(event.target.value)}
                  spellCheck={false}
                  style={{ ...monoTextareaStyle, height: 120, fontSize: 11 }}
                />
                <div style={{ border: '1px solid var(--border)', borderRadius: 8, overflow: 'auto', maxHeight: 300 }}>
                  {diffRows.map((row, index) => (
                    <div
                      key={index}
                      style={{
                        display: 'flex',
                        gap: 10,
                        padding: '1px 12px',
                        background: row.sign === '+' ? 'var(--pass-bg)' : row.sign === '−' ? 'var(--fail-bg)' : 'transparent',
                      }}
                    >
                      <span
                        style={{
                          fontFamily: monoFont,
                          fontSize: 11,
                          width: 10,
                          flex: 'none',
                          color: row.sign === '+' ? 'var(--pass)' : row.sign === '−' ? 'var(--fail)' : 'var(--chip-gray-fg)',
                        }}
                      >
                        {row.sign}
                      </span>
                      <span style={{ fontFamily: monoFont, fontSize: 11, whiteSpace: 'pre', color: 'var(--text2)' }}>{row.text}</span>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}

            {!result.error && tab === 'trace' ? (
              result.trace.length > 0 ? (
                result.trace.map((line, index) => (
                  <div
                    key={index}
                    style={{ display: 'flex', gap: 12, padding: '8px 10px', borderBottom: '1px solid var(--border)' }}
                  >
                    <span
                      style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)', width: 18, textAlign: 'right', flex: 'none' }}
                    >
                      {index + 1}
                    </span>
                    <span style={{ fontFamily: monoFont, fontSize: 11.5, color: 'var(--text2)', flex: 1, minWidth: 0, wordBreak: 'break-word' }}>
                      {line}
                    </span>
                  </div>
                ))
              ) : (
                <span style={{ fontSize: 11, color: 'var(--text4)' }}>
                  No trace output — add <span style={{ fontFamily: monoFont }}>log(...)</span> anywhere in a rule to log a checkpoint.
                </span>
              )
            ) : null}
          </Card>
        </div>
      </div>
    </div>
  );
}
