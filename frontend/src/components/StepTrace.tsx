import { STATUS_LABELS } from '../lib/conformance';
import type { ConformanceStep } from '../types/conformance';

/** Props for {@link StepTrace}. */
export interface StepTraceProps {
  /** Ordered steps (setup, test, teardown) for a single test case. */
  steps: ConformanceStep[];
}

/**
 * Renders the ordered step trace for a test case, including per-step status and
 * any captured request/response detail.
 *
 * Placeholder layout — visual design is handled separately.
 */
export function StepTrace({ steps }: StepTraceProps) {
  if (steps.length === 0) {
    return <p className="step-trace__empty">No steps recorded.</p>;
  }

  return (
    <ol className="step-trace">
      {steps.map((step, index) => (
        <li
          key={index}
          className={`step-trace__step step-trace__step--${step.status}`}
        >
          <span className="step-trace__phase">{step.phase}</span>
          <span className="step-trace__kind">{step.kind}</span>
          <span className="step-trace__label">{step.label ?? step.description}</span>
          <span className="step-trace__status">{STATUS_LABELS[step.status]}</span>
          {step.message ? (
            <span className="step-trace__message">{step.message}</span>
          ) : null}
        </li>
      ))}
    </ol>
  );
}
