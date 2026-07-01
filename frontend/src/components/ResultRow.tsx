import { useState } from 'react';
import { STATUS_LABELS } from '../lib/conformance';
import type { ConformanceResult } from '../types/conformance';
import { StepTrace } from './StepTrace';

/** Props for {@link ResultRow}. */
export interface ResultRowProps {
  /** The test-case result to render. */
  result: ConformanceResult;
}

/**
 * A single test-case row. Expands to reveal the step trace and failure detail.
 *
 * Placeholder layout — visual design is handled separately.
 */
export function ResultRow({ result }: ResultRowProps) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className={`result-row result-row--${result.status}`}>
      <button
        type="button"
        className="result-row__header"
        aria-expanded={expanded}
        onClick={() => setExpanded((value) => !value)}
      >
        <span className="result-row__status">{STATUS_LABELS[result.status]}</span>
        <span className="result-row__name">{result.id}</span>
        <span className="result-row__duration">{result.duration_ms} ms</span>
      </button>

      {expanded ? (
        <div className="result-row__detail">
          {result.error ? (
            <div className="result-row__error">
              <p className="result-row__assertion">{result.error.assertion}</p>
              <p className="result-row__received">{result.error.received}</p>
            </div>
          ) : null}
          <StepTrace steps={result.steps} />
        </div>
      ) : null}
    </div>
  );
}
