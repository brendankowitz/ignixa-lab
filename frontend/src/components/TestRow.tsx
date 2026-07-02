import { useMemo, useState } from 'react';
import { extractAssertions, STATUS_LABELS } from '../lib/conformance';
import type {
  ConformanceHttpRequest,
  ConformanceHttpResponse,
  ConformanceResult,
  ConformanceStep,
} from '../types/conformance';

/** Props for {@link TestRow}. */
export interface TestRowProps {
  result: ConformanceResult;
  /** Display label combining the owning suite and file, shown in the row's meta line. */
  suiteLabel: string;
}

type DetailTab = 'assertions' | 'request' | 'response';

/**
 * A single test-case row. Collapsed, it shows the pass/fail chip, test name,
 * and file context; expanded, it reveals Assertions / Request / Response
 * tabs built from the result's step trace.
 */
export function TestRow({ result, suiteLabel }: TestRowProps) {
  const [expanded, setExpanded] = useState(false);
  const [tab, setTab] = useState<DetailTab>('assertions');

  const assertions = useMemo(() => extractAssertions(result.steps), [result.steps]);
  const operationSteps = useMemo(
    () => result.steps.filter((step) => step.kind === 'operation' && (step.request || step.response)),
    [result.steps],
  );
  // The backend records only its first failing assertion (`result.error`); attach
  // the expected/actual diff to that one row and leave the rest as plain steps.
  const firstFailureIndex = assertions.findIndex((a) => a.status === 'fail' || a.status === 'error');

  return (
    <div className={`test-row test-row--${result.status}`}>
      <button
        type="button"
        className="test-row__header"
        aria-expanded={expanded}
        onClick={() => setExpanded((value) => !value)}
      >
        <span className={`test-row__chip test-row__chip--${result.status}`}>{STATUS_LABELS[result.status]}</span>
        <div className="test-row__text">
          <span className="test-row__name">{result.id}</span>
          <span className="test-row__meta">
            {suiteLabel} · {result.steps.length} steps · {result.duration_ms}ms
          </span>
        </div>
      </button>

      {expanded ? (
        <div className="test-row__detail">
          <div className="test-row__tabs">
            <TabButton label="Assertions" active={tab === 'assertions'} onClick={() => setTab('assertions')} />
            <TabButton label="Request" active={tab === 'request'} onClick={() => setTab('request')} />
            <TabButton label="Response" active={tab === 'response'} onClick={() => setTab('response')} />
          </div>

          {tab === 'assertions' ? (
            <div className="test-row__panel test-row__panel--assertions">
              {assertions.length === 0 ? (
                <p className="test-row__empty">No assertions recorded for this test.</p>
              ) : (
                assertions.map((assertion, index) => {
                  const failing = assertion.status === 'fail' || assertion.status === 'error';
                  const showDiff = failing && index === firstFailureIndex && result.error !== null;
                  return (
                    <div key={index} className={`assertion${showDiff ? ' assertion--diff' : ''}`}>
                      <div className="assertion__header">
                        <span className={`assertion__chip assertion__chip--${assertion.status}`}>
                          {STATUS_LABELS[assertion.status].toUpperCase()}
                        </span>
                        <span className="assertion__label">{assertion.label}</span>
                        {assertion.message && !showDiff ? (
                          <span className="assertion__meta">{assertion.message}</span>
                        ) : null}
                      </div>
                      {showDiff && result.error ? (
                        <>
                          <div className="assertion__diff-grid">
                            <div className="assertion__diff-pane assertion__diff-pane--expected">
                              <span className="assertion__diff-label">EXPECTED</span>
                              <pre>{result.error.assertion ?? '—'}</pre>
                            </div>
                            <div className="assertion__diff-pane assertion__diff-pane--actual">
                              <span className="assertion__diff-label">ACTUAL</span>
                              <pre>{result.error.received ?? '—'}</pre>
                            </div>
                          </div>
                          {assertion.message ? <span className="assertion__hint">{assertion.message}</span> : null}
                        </>
                      ) : null}
                    </div>
                  );
                })
              )}
            </div>
          ) : null}

          {tab === 'request' ? (
            <div className="test-row__panel">
              {operationSteps.length === 0 ? (
                <p className="test-row__empty">No request captured for this test.</p>
              ) : (
                operationSteps.map((step, index) => (
                  <pre key={index} className="test-row__code-block">
                    {stepHeading(step)}
                    {step.request ? `\n\n${formatHttpRequest(step.request)}` : '\n\n(no request captured)'}
                  </pre>
                ))
              )}
            </div>
          ) : null}

          {tab === 'response' ? (
            <div className="test-row__panel">
              {operationSteps.length === 0 ? (
                <p className="test-row__empty">No response captured for this test.</p>
              ) : (
                operationSteps.map((step, index) => (
                  <pre key={index} className="test-row__code-block">
                    {stepHeading(step)}
                    {step.response ? `\n\n${formatHttpResponse(step.response)}` : '\n\n(no response captured)'}
                  </pre>
                ))
              )}
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function TabButton({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button type="button" className={`test-row__tab${active ? ' test-row__tab--active' : ''}`} onClick={onClick}>
      {label}
    </button>
  );
}

/** A short "<phase> · <label>" heading identifying which step a request/response block belongs to. */
function stepHeading(step: ConformanceStep): string {
  const label = step.label ?? step.description ?? 'operation';
  return `${step.phase} · ${label}`;
}

function formatHttpRequest(request: ConformanceHttpRequest): string {
  const headerLines = Object.entries(request.headers).map(([name, value]) => `${name}: ${value}`);
  const lines = [`${request.method} ${request.url}`, ...headerLines];
  return request.body ? `${lines.join('\n')}\n\n${request.body}` : lines.join('\n');
}

function formatHttpResponse(response: ConformanceHttpResponse): string {
  const headerLines = Object.entries(response.headers).map(([name, value]) => `${name}: ${value}`);
  const lines = [`HTTP ${response.statusCode}`, ...headerLines];
  if (response.bodyParseError) {
    lines.push('', `(unparseable body: ${response.bodyParseError})`);
    return lines.join('\n');
  }
  return response.body ? `${lines.join('\n')}\n\n${response.body}` : lines.join('\n');
}
