import { useMemo } from 'react';
import './App.css';
import { HostForm } from './components/HostForm';
import { ProgressPanel } from './components/ProgressPanel';
import { ResultsMatrix } from './components/ResultsMatrix';
import { SuitePicker } from './components/SuitePicker';
import { SummaryCards } from './components/SummaryCards';
import { useConformanceRun } from './hooks/useConformanceRun';
import { useSuiteSelection } from './hooks/useSuiteSelection';
import { countByStatus } from './lib/conformance';

/**
 * Top-level application shell wiring the run workflow together:
 * pick a target server + suites, run them, and view the results matrix.
 *
 * This is the structural skeleton; detailed visual design lives in the CSS and
 * is iterated on separately.
 */
function App() {
  const run = useConformanceRun();
  const selection = useSuiteSelection();

  const suiteIds = useMemo(() => run.suites.map((suite) => suite.id), [run.suites]);
  const running = run.phase === 'running';

  const handleSubmit = (targetUrl: string) => {
    void run.start({ targetUrl, suiteIds: Array.from(selection.selected) });
  };

  const summaryCounts = run.report ? countByStatus(run.report.results) : null;

  return (
    <div className="app">
      <header className="app__header">
        <h1>Ignixa Lab</h1>
        <p>FHIR TestScript conformance runner</p>
      </header>

      <main className="app__main">
        <section className="app__config" aria-label="Run configuration">
          <HostForm
            running={running}
            canSubmit={selection.selected.size > 0}
            onSubmit={handleSubmit}
            onCancel={run.cancel}
          />
          <SuitePicker
            suites={run.suites}
            selected={selection.selected}
            loading={run.suitesLoading}
            error={run.suitesError}
            onToggle={selection.toggle}
            onToggleAll={(select) => selection.toggleAll(suiteIds, select)}
          />
        </section>

        <ProgressPanel
          phase={run.phase}
          suiteCount={selection.selected.size}
          error={run.runError}
        />

        {run.report && summaryCounts ? (
          <section className="app__results" aria-label="Results">
            <SummaryCards counts={summaryCounts} durationMs={run.report.duration_ms} />
            <ResultsMatrix report={run.report} />
          </section>
        ) : null}
      </main>
    </div>
  );
}

export default App;
