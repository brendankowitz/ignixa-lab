import { useMemo } from 'react';
import { FHIR_VERSIONS, type RunConfig } from '../hooks/useRunConfig';
import type { SuiteDescriptor } from '../types/conformance';

/** Props for {@link SetupScreen}. */
export interface SetupScreenProps {
  config: RunConfig;
  suites: SuiteDescriptor[];
  suitesLoading: boolean;
  suitesError: string | null;
  canStart: boolean;
  onStart: () => void;
}

/**
 * Configure-and-launch screen: target endpoint, FHIR version, and the bundled
 * suite checklist (grouped by category), ending in a "Start run" action that
 * kicks off the run and hands off to the Runner screen.
 */
export function SetupScreen({ config, suites, suitesLoading, suitesError, canStart, onStart }: SetupScreenProps) {
  const categories = useMemo(() => groupByCategory(suites), [suites]);
  const allSelected = suites.length > 0 && config.selection.selected.size === suites.length;

  return (
    <div className="setup-screen">
      <div className="setup-screen__intro">
        <h1>Configure conformance run</h1>
        <p>Point Ignixa Lab at a FHIR server, choose suites, and start a run.</p>
      </div>

      <section className="setup-panel" aria-label="Target server">
        <span className="setup-panel__title">Target server</span>

        <div className="setup-field">
          <label className="setup-field__label" htmlFor="endpoint-input">
            Endpoint
          </label>
          <div className="endpoint-input">
            <span className="endpoint-input__prefix">https://</span>
            <input
              id="endpoint-input"
              type="text"
              className="endpoint-input__field"
              placeholder="hapi.fhir.org/baseR4"
              value={config.endpoint}
              onChange={(event) => config.setEndpoint(event.target.value)}
              spellCheck={false}
              autoComplete="off"
            />
          </div>
        </div>

        <div className="setup-field">
          <span className="setup-field__label">FHIR version</span>
          <div className="segmented" role="radiogroup" aria-label="FHIR version">
            {FHIR_VERSIONS.map((version) => (
              <button
                key={version}
                type="button"
                role="radio"
                aria-checked={config.fhirVersion === version}
                className={`segmented__item${config.fhirVersion === version ? ' segmented__item--active' : ''}`}
                onClick={() => config.setFhirVersion(version)}
              >
                {version}
              </button>
            ))}
          </div>
        </div>

        {/* extension point: RunRequest has no auth field, so no Bearer-token control here */}
      </section>

      <section className="setup-panel" aria-label="Test suites">
        <span className="setup-panel__title">Test suites</span>

        {suitesLoading ? <p className="setup-panel__status">Loading suites…</p> : null}
        {suitesError ? <p className="setup-panel__status setup-panel__status--error">{suitesError}</p> : null}
        {!suitesLoading && !suitesError && suites.length === 0 ? (
          <p className="setup-panel__status">No suites available.</p>
        ) : null}

        {!suitesLoading && !suitesError && suites.length > 0 ? (
          <>
            <div className="suite-checklist__toolbar">
              <button
                type="button"
                className="suite-checklist__select-all"
                onClick={() =>
                  config.selection.toggleAll(
                    suites.map((suite) => suite.id),
                    !allSelected,
                  )
                }
              >
                {allSelected ? 'Clear all' : 'Select all'}
              </button>
              <span className="suite-checklist__count">{config.selection.selected.size} selected</span>
            </div>

            {categories.map(([category, categorySuites]) => (
              <div key={category} className="suite-checklist__category">
                <span className="suite-checklist__category-label">{category}</span>
                {categorySuites.map((suite) => {
                  const checked = config.selection.selected.has(suite.id);
                  return (
                    <div
                      key={suite.id}
                      className="suite-checklist__item"
                      role="checkbox"
                      aria-checked={checked}
                      tabIndex={0}
                      onClick={() => config.selection.toggle(suite.id)}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') {
                          event.preventDefault();
                          config.selection.toggle(suite.id);
                        }
                      }}
                    >
                      <span className={`suite-checklist__box${checked ? ' suite-checklist__box--checked' : ''}`}>
                        {checked ? '✓' : ''}
                      </span>
                      <div className="suite-checklist__text">
                        <span className="suite-checklist__name">{suite.name}</span>
                        <span className="suite-checklist__description">{suite.description}</span>
                      </div>
                    </div>
                  );
                })}
              </div>
            ))}
          </>
        ) : null}
      </section>

      <div className="setup-screen__actions">
        <button type="button" className="setup-screen__start-button" disabled={!canStart} onClick={onStart}>
          ▶ Start run · {config.selection.selected.size} {config.selection.selected.size === 1 ? 'suite' : 'suites'}
        </button>
      </div>
    </div>
  );
}

/** Groups suites by category, preserving first-seen category order. */
function groupByCategory(suites: SuiteDescriptor[]): [string, SuiteDescriptor[]][] {
  const groups = new Map<string, SuiteDescriptor[]>();
  for (const suite of suites) {
    const bucket = groups.get(suite.category);
    if (bucket) {
      bucket.push(suite);
    } else {
      groups.set(suite.category, [suite]);
    }
  }
  return Array.from(groups);
}
