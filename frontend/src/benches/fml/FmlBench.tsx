import { useMemo, useState, type CSSProperties } from 'react';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoTextareaStyle, primaryButtonStyle, sectionLabelStyle } from '../components/styles';
import { diffLines } from './diffLines';
import { runFml } from './fmlEngine';
import { DEFAULT_EXPECTED_TEXT, DEFAULT_MAP_TEXT, DEFAULT_SOURCE_TEXT } from './fmlFixtures';
import { highlightFml } from './fmlHighlight';

type FmlTab = 'output' | 'diff' | 'log';

const TAB_ITEMS: PillItem<FmlTab>[] = [
  { id: 'output', label: 'Output' },
  { id: 'diff', label: 'Diff vs expected' },
  { id: 'log', label: 'Execution log' },
];

const twoColumnStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'minmax(420px,52%) 1fr',
  gap: 14,
  alignItems: 'start',
};

export function FmlBench() {
  const [mapText, setMapText] = useState(DEFAULT_MAP_TEXT);
  const [sourceText, setSourceText] = useState(DEFAULT_SOURCE_TEXT);
  const [expectedText, setExpectedText] = useState(DEFAULT_EXPECTED_TEXT);
  const [tab, setTab] = useState<FmlTab>('output');
  const [result, setResult] = useState(() => runFml(DEFAULT_MAP_TEXT, DEFAULT_SOURCE_TEXT));

  const highlightedLines = useMemo(() => highlightFml(mapText), [mapText]);
  const outputText = result.output ? JSON.stringify(result.output, null, 2) : '—';
  const diffRows = result.output ? diffLines(outputText, expectedText) : [];

  const runMap = () => setResult(runFml(mapText, sourceText));

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>FHIR Mapping Language</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>Author a StructureMap in FML and debug it rule by rule.</span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>mock engine · ignixa-fml 0.1</span>
        <button type="button" onClick={runMap} style={primaryButtonStyle}>
          ▶ Run map
        </button>
      </div>

      <div style={twoColumnStyle}>
        <Card>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Map source · .fml</span>
            <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>{result.mapName}</span>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 1, padding: '4px 0' }}>
            {highlightedLines.map((line, index) => (
              <div key={index} style={{ height: 19, whiteSpace: 'pre', fontFamily: monoFont, fontSize: 12 }}>
                {line.segments.map((segment, segmentIndex) => (
                  <span key={segmentIndex} style={{ color: segment.color, whiteSpace: 'pre' }}>
                    {segment.text}
                  </span>
                ))}
              </div>
            ))}
          </div>
          <textarea
            value={mapText}
            onChange={(event) => setMapText(event.target.value)}
            spellCheck={false}
            wrap="off"
            style={{ ...monoTextareaStyle, height: 300 }}
          />
        </Card>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <Card>
            <span style={sectionLabelStyle}>Source resource</span>
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
                {result.output ? `${result.applied} applied · ${result.skipped} skipped` : ''}
              </span>
            </div>

            {result.error ? <ErrorBanner message={result.error} /> : null}

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
                {outputText}
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

            {tab === 'log'
              ? result.log.map((row, index) => (
                  <div key={index} style={{ display: 'flex', gap: 12, alignItems: 'baseline', padding: '8px 10px', borderBottom: '1px solid var(--border)' }}>
                    <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)', width: 18, textAlign: 'right', flex: 'none' }}>{index + 1}</span>
                    <span style={{ fontFamily: monoFont, fontSize: 11.5, fontWeight: 600, color: 'var(--accent)', flex: 'none', minWidth: 110 }}>{row.rule}</span>
                    <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text2)', flex: 'none' }}>
                      {row.src} → {row.tgt}
                    </span>
                    <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)', flex: 1, minWidth: 0, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                      {row.val}
                    </span>
                    <span
                      style={{
                        fontFamily: monoFont,
                        fontSize: 9.5,
                        fontWeight: 600,
                        padding: '2px 8px',
                        borderRadius: 99,
                        flex: 'none',
                        background: row.status === 'applied' ? 'var(--pass-bg)' : row.status === 'skipped' ? 'var(--chip-gray-bg)' : 'var(--fail-bg)',
                        color: row.status === 'applied' ? 'var(--pass)' : row.status === 'skipped' ? 'var(--chip-gray-fg)' : 'var(--fail)',
                      }}
                    >
                      {row.status}
                    </span>
                  </div>
                ))
              : null}
          </Card>
        </div>
      </div>
    </div>
  );
}
