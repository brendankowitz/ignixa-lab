import { useMemo, useState } from 'react';
import { extractAssertions, STATUS_LABELS } from '../lib/conformance';
import type { ConformanceResult, ConformanceStep } from '../types/conformance';
import { HttpRequestView, HttpResponseView } from './HttpMessage';

/** Props for {@link TestRow}. */
export interface TestRowProps {
  result: ConformanceResult;
  /** Display label combining the owning suite and file, shown in the row's meta line. */
  suiteLabel: string;
}

type DetailTab = 'assertions' | 'steps';

/**
 * A single test-case row. Collapsed, it shows the pass/fail chip, test name,
 * and file context; expanded, it reveals Assertions and Steps tabs built from
 * the result's step trace.
 */
export function TestRow({ result, suiteLabel }: TestRowProps) {
  const [expanded, setExpanded] = useState(false);
  const [tab, setTab] = useState<DetailTab>('assertions');

  const assertions = useMemo(() => extractAssertions(result.steps), [result.steps]);
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
            <TabButton label="Steps" active={tab === 'steps'} onClick={() => setTab('steps')} />
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

          {tab === 'steps' ? (
            <div className="test-row__panel step-list">
              {result.steps.length === 0 ? (
                <p className="test-row__empty">No steps recorded for this test.</p>
              ) : (
                result.steps.map((step, index) => (
                  <StepRow key={`${step.phase}-${step.kind}-${index}`} step={step} index={index} />
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

/**
 * A single row in the Steps tab's walkthrough: a compact, collapsed-by-default
 * summary (status chip, title, phase/kind/duration meta) that expands in
 * place to show the captured request/response when present, or a plain
 * message otherwise (in practice: request/response for operation steps,
 * message for assertion steps).
 */
function StepRow({ step, index }: { step: ConformanceStep; index: number }) {
  const [open, setOpen] = useState(false);
  const hasExchange = step.request !== null || step.response !== null;
  const hasMembers = Boolean(step.members?.length);
  const hasDetail = hasExchange || Boolean(step.message) || hasMembers;

  const title =
    step.kind === 'operation' && step.request
      ? `${step.request.method.toUpperCase()} ${shortUrl(step.request.url)}`
      : (step.label ?? step.description ?? `Step ${index + 1}`);
  const statusCode = step.kind === 'operation' ? step.response?.statusCode : null;

  const headerContent = (
    <>
      <span className={`step__chip step__chip--${step.status}`}>{STATUS_LABELS[step.status]}</span>
      <div className="step__text">
        <span className="step__title-row">
          <span className="step__title">{title}</span>
          {statusCode != null ? <span className="step__status-code">{statusCode}</span> : null}
        </span>
        <span className="step__meta">
          {step.phase} · {step.kind} · {step.duration_ms}ms
        </span>
      </div>
      {hasDetail ? (
        <span className={`step__chevron${open ? ' step__chevron--open' : ''}`} aria-hidden="true">
          ▸
        </span>
      ) : null}
    </>
  );

  return (
    <div className={`step step--${step.status}`}>
      {hasDetail ? (
        <button
          type="button"
          className="step__header"
          aria-expanded={open}
          onClick={() => setOpen((value) => !value)}
        >
          {headerContent}
        </button>
      ) : (
        <div className="step__header step__header--static">{headerContent}</div>
      )}

      {hasDetail && open ? (
        <div className="step__body">
          {step.request ? <HttpRequestView request={step.request} /> : null}
          {step.response ? <HttpResponseView response={step.response} /> : null}
          {step.message && (!hasExchange || step.status !== 'pass') ? (
            <p className="step__message">{step.message}</p>
          ) : null}
          {hasMembers ? (
            <div className="step__members">
              {step.members!.map((member, memberIndex) => (
                <div key={memberIndex} className="step__member">
                  <div className="step__member-header">
                    <span
                      className={`step__member-chip${
                        member.applicable ? (member.passed ? ' step__member-chip--pass' : ' step__member-chip--fail') : ''
                      }`}
                    >
                      {member.applicable ? (member.passed ? 'PASS' : 'FAIL') : 'N/A'}
                    </span>
                    <span className="step__member-label">{member.description ?? 'Alternative'}</span>
                  </div>
                  {member.message ? <span className="step__member-message">{member.message}</span> : null}
                </div>
              ))}
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

/**
 * Reduces a captured request URL down to its path + query for a compact step
 * title; falls back to the raw string if it isn't a parseable absolute URL.
 */
function shortUrl(url: string): string {
  try {
    const parsed = new URL(url);
    return `${parsed.pathname}${parsed.search}`;
  } catch {
    return url;
  }
}
