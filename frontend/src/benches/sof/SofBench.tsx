import { useState, type CSSProperties } from 'react';
import { Card, ErrorBanner } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoTextareaStyle, primaryButtonStyle, sectionLabelStyle } from '../components/styles';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import { runSof } from './sofEngine';
import { DEFAULT_RESOURCES_TEXT, DEFAULT_VIEW_DEFINITION_TEXT } from './sofFixtures';

export function SofBench() {
  const stacked = useIsNarrowViewport(720);
  const twoColumnStyle: CSSProperties = {
    display: 'grid',
    gridTemplateColumns: stacked ? '1fr' : 'minmax(380px,44%) 1fr',
    gap: 14,
    alignItems: 'start',
  };

  const [viewDefinitionText, setViewDefinitionText] = useState(DEFAULT_VIEW_DEFINITION_TEXT);
  const [resourcesText, setResourcesText] = useState(DEFAULT_RESOURCES_TEXT);
  const [result, setResult] = useState(() => runSof(DEFAULT_VIEW_DEFINITION_TEXT, DEFAULT_RESOURCES_TEXT));

  const runView = () => setResult(runSof(viewDefinitionText, resourcesText));
  const gridColumns = result.columns.length ? `repeat(${result.columns.length}, minmax(110px, 1fr))` : '1fr';

  return (
    <div style={{ maxWidth: 1380, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>SQL on FHIR</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>Run a ViewDefinition over resources and inspect the flattened table.</span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>mock engine · ignixa-views 0.1</span>
        <button type="button" onClick={runView} style={primaryButtonStyle}>
          ▶ Run view
        </button>
      </div>

      <div style={twoColumnStyle}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14, minWidth: 0 }}>
          <Card>
            <span style={sectionLabelStyle}>ViewDefinition</span>
            <textarea
              value={viewDefinitionText}
              onChange={(event) => setViewDefinitionText(event.target.value)}
              spellCheck={false}
              style={{ ...monoTextareaStyle, height: 320, fontSize: 11.5 }}
            />
          </Card>
          <Card>
            <span style={sectionLabelStyle}>Resources · JSON array</span>
            <textarea
              value={resourcesText}
              onChange={(event) => setResourcesText(event.target.value)}
              spellCheck={false}
              style={{ ...monoTextareaStyle, height: 220, fontSize: 11.5 }}
            />
          </Card>
        </div>

        <Card style={{ minHeight: 400, minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Result table</span>
            <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>{result.meta}</span>
          </div>

          {result.error ? <ErrorBanner message={result.error} /> : null}

          <div style={{ border: '1px solid var(--border)', borderRadius: 8, overflow: 'auto' }}>
            <div style={{ display: 'grid', gridTemplateColumns: gridColumns, background: 'var(--panel2)', borderBottom: '1px solid var(--border)' }}>
              {result.columns.map((column) => (
                <span
                  key={column}
                  style={{
                    padding: '8px 12px',
                    fontFamily: monoFont,
                    fontSize: 10,
                    fontWeight: 600,
                    letterSpacing: '.08em',
                    textTransform: 'uppercase',
                    color: 'var(--text2)',
                    borderRight: '1px solid var(--border)',
                  }}
                >
                  {column}
                </span>
              ))}
            </div>
            {result.rows.map((row, rowIndex) => (
              <div key={rowIndex} style={{ display: 'grid', gridTemplateColumns: gridColumns, borderBottom: '1px solid var(--border)' }}>
                {result.columns.map((column) => {
                  const value = row[column];
                  return (
                    <span
                      key={column}
                      style={{
                        padding: '7px 12px',
                        fontFamily: monoFont,
                        fontSize: 11.5,
                        color: value === null || value === undefined ? 'var(--text4)' : 'var(--text)',
                        borderRight: '1px solid var(--border)',
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                      }}
                    >
                      {value === null || value === undefined ? '∅' : String(value)}
                    </span>
                  );
                })}
              </div>
            ))}
          </div>
          <span style={{ fontSize: 11, color: 'var(--text4)' }}>
            Columns come from <span style={{ fontFamily: monoFont }}>select[].column[].path</span> (FHIRPath);{' '}
            <span style={{ fontFamily: monoFont }}>forEach</span> unnests one row per item.
          </span>
        </Card>
      </div>
    </div>
  );
}
