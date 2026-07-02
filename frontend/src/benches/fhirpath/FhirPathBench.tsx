import { useState, type CSSProperties } from 'react';
import { Card, ErrorBanner, Pills, type PillItem } from '../components/primitives';
import { engineBadgeStyle, monoFont, monoInputStyle, monoTextareaStyle, sectionLabelStyle, chipStyle } from '../components/styles';
import { DEFAULT_EXPRESSION, EXAMPLE_EXPRESSIONS, SAMPLE_RESOURCES, type SampleId } from './sampleResources';
import { useFhirPathEval } from './useFhirPathEval';
import type { FhirVersion, FpAstNode, FpVariable } from './fhirPathTypes';

const VERSION_ITEMS: PillItem[] = [
  { id: 'stu3', label: 'STU3' },
  { id: 'r4', label: 'R4' },
  { id: 'r4b', label: 'R4B' },
  { id: 'r5', label: 'R5' },
  { id: 'r6', label: 'R6' },
];

const RESULT_TAB_ITEMS: PillItem[] = [
  { id: 'results', label: 'Results' },
  { id: 'trace', label: 'Trace' },
  { id: 'ast', label: 'Parse tree' },
];

type ResultTab = 'results' | 'trace' | 'ast';

function typeChipColors(type: string): { bg: string; fg: string } {
  if (['string', 'date', 'dateTime'].includes(type)) {
    return { bg: 'var(--pass-bg)', fg: 'var(--pass)' };
  }
  if (['integer', 'decimal', 'boolean'].includes(type)) {
    return { bg: 'var(--chip-teal-bg)', fg: 'var(--chip-teal-fg)' };
  }
  if (['null', 'collection', 'object'].includes(type)) {
    return { bg: 'var(--chip-gray-bg)', fg: 'var(--chip-gray-fg)' };
  }
  return { bg: 'var(--chip-vio-bg)', fg: 'var(--chip-vio-fg)' };
}

function astChipColors(expressionType: string): { bg: string; fg: string } {
  switch (expressionType) {
    case 'FunctionCallExpression':
      return { bg: 'var(--chip-pink-bg)', fg: 'var(--chip-pink-fg)' };
    case 'ChildExpression':
    case 'PropertyAccessExpression':
      return { bg: 'var(--chip-teal-bg)', fg: 'var(--chip-teal-fg)' };
    case 'ConstantExpression':
      return { bg: 'var(--chip-amb-bg)', fg: 'var(--chip-amb-fg)' };
    case 'BinaryExpression':
    case 'UnaryExpression':
      return { bg: 'var(--chip-ind-bg)', fg: 'var(--chip-ind-fg)' };
    case 'VariableRefExpression':
      return { bg: 'var(--chip-red-bg)', fg: 'var(--fail)' };
    default:
      return { bg: 'var(--chip-gray-bg)', fg: 'var(--chip-gray2-fg)' };
  }
}

function AstRows({ node, depth }: { node: FpAstNode; depth: number }) {
  const colors = astChipColors(node.expressionType);
  return (
    <>
      <div style={{ padding: `3px 0 3px ${depth * 18 + 2}px`, display: 'flex', gap: 8, alignItems: 'baseline' }}>
        <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)' }}>├─</span>
        <span style={chipStyle(colors.bg, colors.fg)}>{node.expressionType}</span>
        <span style={{ fontFamily: monoFont, fontSize: 12, color: 'var(--text)' }}>
          {node.name}
          {node.returnType ? ` : ${node.returnType}` : ''}
        </span>
      </div>
      {node.arguments.map((child, index) => (
        <AstRows key={index} node={child} depth={depth + 1} />
      ))}
    </>
  );
}

const twoColumnStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'minmax(340px,42%) 1fr',
  gap: 14,
  alignItems: 'start',
};

export function FhirPathBench() {
  const [version, setVersion] = useState<FhirVersion>('r4');
  const [expression, setExpression] = useState(DEFAULT_EXPRESSION);
  const [context, setContext] = useState('');
  const [sampleId, setSampleId] = useState<SampleId>('patient');
  const [resourceText, setResourceText] = useState(() => JSON.stringify(SAMPLE_RESOURCES[0].data, null, 2));
  const [variables, setVariables] = useState<FpVariable[]>([]);
  const [resultTab, setResultTab] = useState<ResultTab>('results');

  const { result, isLoading } = useFhirPathEval({ version, expression, context, resourceText, variables });

  const updateVariable = (index: number, patch: Partial<FpVariable>) =>
    setVariables((current) => current.map((variable, i) => (i === index ? { ...variable, ...patch } : variable)));

  const removeVariable = (index: number) => setVariables((current) => current.filter((_, i) => i !== index));

  return (
    <div style={{ maxWidth: 1280, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>FHIRPath</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>
          Evaluate expressions against a resource — results, trace, and parse tree.
        </span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>{result.evaluator || (isLoading ? 'evaluating…' : 'ignixa-lab')}</span>
      </div>

      <Card>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span style={sectionLabelStyle}>Expression</span>
          <textarea
            value={expression}
            onChange={(event) => setExpression(event.target.value)}
            spellCheck={false}
            rows={2}
            style={monoTextareaStyle}
          />
        </div>

        <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6, flex: 1, minWidth: 260 }}>
            <span style={sectionLabelStyle}>
              Context expression <span style={{ textTransform: 'none', letterSpacing: 0, color: 'var(--text4)' }}>· optional, evaluates per item</span>
            </span>
            <input
              value={context}
              onChange={(event) => setContext(event.target.value)}
              spellCheck={false}
              placeholder="e.g. name"
              style={monoInputStyle}
            />
          </div>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center', paddingBottom: 2 }}>
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>Examples</span>
            {EXAMPLE_EXPRESSIONS[sampleId].map((example) => (
              <span
                key={example}
                onClick={() => setExpression(example)}
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
              </span>
            ))}
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={sectionLabelStyle}>Variables</span>
            <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--text4)' }}>%resource · %context built-in</span>
            <span
              onClick={() => setVariables((current) => [...current, { name: '', value: '' }])}
              style={{ fontSize: 11.5, fontWeight: 600, color: 'var(--accent)', cursor: 'pointer' }}
            >
              + Add variable
            </span>
          </div>
          {variables.map((variable, index) => (
            <div key={index} style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <span style={{ fontFamily: monoFont, fontSize: 12, color: 'var(--text3)' }}>%</span>
              <input
                value={variable.name}
                onChange={(event) => updateVariable(index, { name: event.target.value })}
                placeholder="name"
                spellCheck={false}
                style={{ ...monoInputStyle, width: 140 }}
              />
              <input
                value={variable.value}
                onChange={(event) => updateVariable(index, { value: event.target.value })}
                placeholder="value (JSON or string)"
                spellCheck={false}
                style={{ ...monoInputStyle, flex: 1 }}
              />
              <span
                onClick={() => removeVariable(index)}
                style={{ width: 24, height: 24, display: 'grid', placeItems: 'center', borderRadius: 6, color: 'var(--text4)', cursor: 'pointer', fontSize: 12 }}
              >
                ✕
              </span>
            </div>
          ))}
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <span style={sectionLabelStyle}>FHIR version</span>
          <Pills items={VERSION_ITEMS} activeId={version} onChange={(id) => setVersion(id as FhirVersion)} />
        </div>
      </Card>

      <div style={twoColumnStyle}>
        <Card>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Test resource</span>
            {SAMPLE_RESOURCES.map((sample) => (
              <span
                key={sample.id}
                onClick={() => {
                  setSampleId(sample.id);
                  setResourceText(JSON.stringify(sample.data, null, 2));
                }}
                style={{
                  fontSize: 11,
                  fontWeight: 600,
                  padding: '4px 11px',
                  borderRadius: 99,
                  cursor: 'pointer',
                  background: sampleId === sample.id ? 'var(--chip-vio-bg)' : 'var(--panel)',
                  color: sampleId === sample.id ? 'var(--chip-vio-fg)' : 'var(--text3)',
                  border: `1px solid ${sampleId === sample.id ? 'var(--accent-border)' : 'var(--border2)'}`,
                }}
              >
                {sample.label}
              </span>
            ))}
          </div>
          <textarea
            value={resourceText}
            onChange={(event) => setResourceText(event.target.value)}
            spellCheck={false}
            style={{ ...monoTextareaStyle, minHeight: 520, fontSize: 11.5 }}
          />
        </Card>

        <Card style={{ minHeight: 400 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <Pills items={RESULT_TAB_ITEMS} activeId={resultTab} onChange={(id) => setResultTab(id as ResultTab)} />
            <div style={{ flex: 1 }} />
            {isLoading ? <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>evaluating…</span> : null}
          </div>

          {result.error ? <ErrorBanner message={result.error} /> : null}

          {!result.error && resultTab === 'results'
            ? result.groups.map((group, groupIndex) => (
                <div key={groupIndex} style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  {group.label ? (
                    <span style={{ fontFamily: monoFont, fontSize: 10, color: 'var(--accent)', paddingTop: 4 }}>{group.label}</span>
                  ) : null}
                  {group.items.length === 0 ? (
                    <div
                      style={{
                        padding: '8px 12px',
                        borderRadius: 8,
                        border: '1px dashed var(--border2)',
                        fontFamily: monoFont,
                        fontSize: 11.5,
                        color: 'var(--text4)',
                      }}
                    >
                      empty collection · {'{ }'}
                    </div>
                  ) : (
                    group.items.map((item, itemIndex) => {
                      const colors = typeChipColors(item.type);
                      return (
                        <div
                          key={itemIndex}
                          style={{
                            display: 'flex',
                            gap: 12,
                            alignItems: 'flex-start',
                            padding: '9px 12px',
                            borderRadius: 8,
                            background: 'var(--code)',
                            border: '1px solid var(--border)',
                          }}
                        >
                          <span style={chipStyle(colors.bg, colors.fg)}>{item.type}</span>
                          <pre style={{ margin: 0, flex: 1, minWidth: 0, fontFamily: monoFont, fontSize: 12, lineHeight: 1.55, whiteSpace: 'pre-wrap', wordBreak: 'break-word', color: 'var(--text)' }}>
                            {item.text}
                          </pre>
                        </div>
                      );
                    })
                  )}
                </div>
              ))
            : null}

          {!result.error && resultTab === 'trace'
            ? result.trace.map((row, rowIndex) => (
                <div key={rowIndex} style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                  <span style={{ fontFamily: monoFont, fontSize: 11, fontWeight: 600, color: 'var(--chip-amb-fg)' }}>trace('{row.label}')</span>
                  {row.items.map((item, itemIndex) => (
                    <pre key={itemIndex} style={{ margin: '0 0 0 16px', fontFamily: monoFont, fontSize: 11.5, color: 'var(--text2)' }}>
                      {item.type}: {item.text}
                    </pre>
                  ))}
                </div>
              ))
            : null}
          {!result.error && resultTab === 'trace' && result.trace.length === 0 ? (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>
              No trace output — add <span style={{ fontFamily: monoFont }}>.trace('label')</span> anywhere in the expression to log a checkpoint.
            </span>
          ) : null}

          {!result.error && resultTab === 'ast' && result.ast ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 1, padding: '4px 2px' }}>
              <AstRows node={result.ast} depth={0} />
            </div>
          ) : null}
          {!result.error && resultTab === 'ast' && !result.ast ? (
            <span style={{ fontSize: 11, color: 'var(--text4)' }}>
              No parse tree yet — evaluate an expression to see its structure.
            </span>
          ) : null}
        </Card>
      </div>
    </div>
  );
}
