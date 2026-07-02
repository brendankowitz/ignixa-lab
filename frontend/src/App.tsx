import { useCallback, useState } from 'react';
import './App.css';
import { ReportScreen } from './components/ReportScreen';
import { RunnerScreen } from './components/RunnerScreen';
import { SetupScreen } from './components/SetupScreen';
import { TopBar, type TabId } from './components/TopBar';
import { useConformanceRun } from './hooks/useConformanceRun';
import { useRunConfig } from './hooks/useRunConfig';
import { useTheme } from './hooks/useTheme';

/**
 * Top-level application shell: a tabbed Setup / Runner / Report workflow over
 * a single shared run configuration and conformance-run state. Tab state is
 * component-local (no router) since the app has no need for deep-linkable
 * screens.
 */
function App() {
  const theme = useTheme();
  const config = useRunConfig();
  const run = useConformanceRun();

  const [activeTab, setActiveTab] = useState<TabId>('setup');
  const [failingOnly, setFailingOnly] = useState(false);

  const running = run.phase === 'running';
  const canStart = config.targetUrl !== '' && config.selection.selected.size > 0 && !running;

  const startRun = useCallback(() => {
    if (!canStart) {
      return;
    }
    void run.start({
      targetUrl: config.targetUrl,
      fhirVersion: config.fhirVersion,
      suiteIds: Array.from(config.selection.selected),
    });
    setActiveTab('runner');
  }, [canStart, config, run]);

  const viewFailing = useCallback(() => {
    setFailingOnly(true);
    setActiveTab('runner');
  }, []);

  return (
    <div className="app-shell" style={theme.variables}>
      <TopBar
        activeTab={activeTab}
        onTabChange={setActiveTab}
        serverHost={config.endpoint}
        fhirVersion={config.fhirVersion}
        theme={theme}
        running={running}
        canStart={canStart}
        onStart={startRun}
        onStop={run.cancel}
      />

      <main className="app-shell__main">
        {activeTab === 'setup' ? (
          <SetupScreen
            config={config}
            suites={run.suites}
            suitesLoading={run.suitesLoading}
            suitesError={run.suitesError}
            canStart={canStart}
            onStart={startRun}
          />
        ) : null}

        {activeTab === 'runner' ? (
          <RunnerScreen
            run={run}
            suites={run.suites}
            failingOnly={failingOnly}
            onFailingOnlyChange={setFailingOnly}
            onViewReport={() => setActiveTab('report')}
          />
        ) : null}

        {activeTab === 'report' ? (
          <ReportScreen report={run.report} suites={run.suites} onViewFailing={viewFailing} />
        ) : null}
      </main>
    </div>
  );
}

export default App;
