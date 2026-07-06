import { useMemo, useState } from 'react';
import type { RunConfig } from '../hooks/useRunConfig';
import { testScriptGithubUrl } from '../lib/github';
import type { SuiteDescriptor } from '../types/conformance';

/** Tri-state of a group's checkbox derived from how many of its suites are selected. */
type GroupState = 'none' | 'some' | 'all';

/** Strips a leading "Category/" prefix so suites read cleanly under their group heading. */
function shortName(suite: SuiteDescriptor): string {
  const slash = suite.name.indexOf('/');
  return slash >= 0 ? suite.name.slice(slash + 1) : suite.name;
}

/** Props for {@link SetupScreen}. */
export interface SetupScreenProps {
  config: RunConfig;
  suites: SuiteDescriptor[];
  suitesLoading: boolean;
  suitesError: string | null;
  /** Commit to link testscript fixtures against; null falls back to `main` (see `testScriptGithubUrl`). */
  testScriptsRevision: string | null;
  canStart: boolean;
  onStart: () => void;
}

/**
 * Configure-and-launch screen: target endpoint and the bundled suite checklist
 * (grouped by category), ending in a "Start run" action that kicks off the run
 * and hands off to the Runner screen. There is no FHIR version field — the
 * version used for gating is detected from the target's own CapabilityStatement
 * once the run executes, not chosen here.
 */
export function SetupScreen({
  config,
  suites,
  suitesLoading,
  suitesError,
  testScriptsRevision,
  canStart,
  onStart,
}: SetupScreenProps) {
  const categories = useMemo(() => groupByCategory(suites), [suites]);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const selected = config.selection.selected;
  const allIds = useMemo(() => suites.map((suite) => suite.id), [suites]);
  const selectedTestTotal = useMemo(
    () => suites.reduce((sum, suite) => (selected.has(suite.id) ? sum + suite.testCount : sum), 0),
    [suites, selected],
  );

  const toggleExpanded = (id: string) =>
    setExpanded((previous) => {
      const next = new Set(previous);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });

  return (
    <div className="setup-screen">
      <div className="setup-screen__intro">
        <h1>Configure conformance run</h1>
        <p>Point Conformance Testing at a FHIR server, choose suites, and start a run.</p>
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
            <button
              type="button"
              className="endpoint-input__run"
              disabled={!canStart}
              title="Start run"
              aria-label="Start run"
              onClick={onStart}
            >
              ▶
            </button>
          </div>
        </div>

        {/* extension point: RunRequest has no auth field, so no Bearer-token control here */}
      </section>

      <section className="setup-panel" aria-label="Test suites">
        <div className="suite-picker__header">
          <span className="setup-panel__title">Test suites</span>
          <span className="suite-picker__count">{selected.size} selected</span>
          <button
            type="button"
            className="suite-picker__action"
            disabled={suites.length === 0}
            onClick={() => config.selection.toggleAll(allIds, true)}
          >
            Select all
          </button>
          <button
            type="button"
            className="suite-picker__action suite-picker__action--muted"
            disabled={selected.size === 0}
            onClick={config.selection.clear}
          >
            Clear
          </button>
        </div>

        {suitesLoading ? <p className="setup-panel__status">Loading suites…</p> : null}
        {suitesError ? <p className="setup-panel__status setup-panel__status--error">{suitesError}</p> : null}
        {!suitesLoading && !suitesError && suites.length === 0 ? (
          <p className="setup-panel__status">No suites available.</p>
        ) : null}

        {!suitesLoading && !suitesError && suites.length > 0
          ? categories.map(([category, categorySuites]) => {
              const groupIds = categorySuites.map((suite) => suite.id);
              const selCount = groupIds.filter((id) => selected.has(id)).length;
              const groupState: GroupState =
                selCount === 0 ? 'none' : selCount === groupIds.length ? 'all' : 'some';
              return (
                <div key={category} className="suite-group">
                  <div className="suite-group__header">
                    <button
                      type="button"
                      role="checkbox"
                      aria-checked={groupState === 'all'}
                      aria-label={`Select all ${category} suites`}
                      className={`suite-check suite-check--${groupState}`}
                      onClick={() => config.selection.setMany(groupIds, groupState !== 'all')}
                    >
                      {groupState === 'all' ? '✓' : groupState === 'some' ? '–' : ''}
                    </button>
                    <span className="suite-group__name">{category}</span>
                    <span className="suite-group__meta">
                      {categorySuites.length} {categorySuites.length === 1 ? 'suite' : 'suites'}
                    </span>
                    <span className="suite-group__sel">
                      {selCount}/{groupIds.length}
                    </span>
                  </div>

                  {categorySuites.map((suite) => {
                    const checked = selected.has(suite.id);
                    const isOpen = expanded.has(suite.id);
                    const hasNotes = Boolean(suite.description);
                    return (
                      <div key={suite.id} className="suite-row-wrap">
                        <div className="suite-row">
                          <button
                            type="button"
                            role="checkbox"
                            aria-checked={checked}
                            aria-label={suite.name}
                            className={`suite-check${checked ? ' suite-check--all' : ''}`}
                            onClick={() => config.selection.toggle(suite.id)}
                          >
                            {checked ? '✓' : ''}
                          </button>
                          <button
                            type="button"
                            className="suite-row__name"
                            onClick={() => config.selection.toggle(suite.id)}
                          >
                            {shortName(suite)}
                          </button>
                          <span className="suite-row__summary">{suite.description}</span>
                          <a
                            className="suite-row__fixture-link"
                            href={testScriptGithubUrl(suite.file, testScriptsRevision ?? undefined)}
                            target="_blank"
                            rel="noreferrer"
                            title={`View ${suite.file} on GitHub`}
                          >
                            Fixture ↗
                          </a>
                          <span className="suite-row__count">
                            {suite.testCount} {suite.testCount === 1 ? 'test' : 'tests'}
                          </span>
                          {hasNotes ? (
                            <button
                              type="button"
                              className="suite-row__expand"
                              aria-expanded={isOpen}
                              title="Tests & implementation notes"
                              onClick={() => toggleExpanded(suite.id)}
                            >
                              {isOpen ? '▾' : '▸'}
                            </button>
                          ) : (
                            <span className="suite-row__expand-spacer" />
                          )}
                        </div>
                        {isOpen ? (
                          <div className="suite-row__detail">
                            {suite.description ? (
                              <div className="suite-row__notes">{suite.description}</div>
                            ) : null}
                            {suite.tests.length > 0 ? (
                              <ol className="suite-tests">
                                {suite.tests.map((test, index) => (
                                  <li
                                    key={index}
                                    className="suite-tests__item"
                                    title={test.description ?? undefined}
                                  >
                                    <span className="suite-tests__idx">{index + 1}</span>
                                    <span className="suite-tests__name">{test.name}</span>
                                  </li>
                                ))}
                              </ol>
                            ) : null}
                          </div>
                        ) : null}
                      </div>
                    );
                  })}
                </div>
              );
            })
          : null}
      </section>

      <div className="setup-screen__actions">
        <button type="button" className="setup-screen__start-button" disabled={!canStart} onClick={onStart}>
          ▶ Start run · {selected.size} {selected.size === 1 ? 'suite' : 'suites'} ·{' '}
          {selectedTestTotal} {selectedTestTotal === 1 ? 'test' : 'tests'}
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
