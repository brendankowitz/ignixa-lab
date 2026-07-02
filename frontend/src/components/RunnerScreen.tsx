import { useCallback, useMemo, useState } from 'react';
import type { ConformanceRunState } from '../hooks/useConformanceRun';
import { countByStatus, rollupCounts } from '../lib/conformance';
import type { SuiteDescriptor } from '../types/conformance';
import { RunnerStatusBar } from './RunnerStatusBar';
import { SuiteTree } from './SuiteTree';
import { TestList } from './TestList';

/** Props for {@link RunnerScreen}. */
export interface RunnerScreenProps {
  run: ConformanceRunState;
  /** Full suite catalog, used to resolve suite IDs to display names. */
  suites: SuiteDescriptor[];
  failingOnly: boolean;
  onFailingOnlyChange: (value: boolean) => void;
  onViewReport: () => void;
}

/**
 * Live/finished run view: a status strip, a suite tree that doubles as a
 * filter, and the test list itself. Before any run has ever started, this
 * renders a plain prompt — there is nothing real to show yet.
 */
export function RunnerScreen({ run, suites, failingOnly, onFailingOnlyChange, onViewReport }: RunnerScreenProps) {
  const [selectedSuiteId, setSelectedSuiteId] = useState<string | null>(null);

  const suiteById = useMemo(() => new Map(suites.map((suite) => [suite.id, suite])), [suites]);
  const suiteName = useCallback((suiteId: string) => suiteById.get(suiteId)?.name ?? suiteId, [suiteById]);

  // `suiteStatuses` is populated (in run order) as soon as `start` is called,
  // so its key order is exactly the suite tree's display order.
  const runSuiteIds = useMemo(() => Array.from(run.suiteStatuses.keys()), [run.suiteStatuses]);

  if (runSuiteIds.length === 0) {
    return (
      <div className="runner-screen runner-screen--empty">
        <p>Configure a run on the Setup screen to get started.</p>
      </div>
    );
  }

  const allResults = run.report?.results ?? [];
  const scopedResults = selectedSuiteId ? allResults.filter((result) => result.suite === selectedSuiteId) : allResults;
  const tallies = rollupCounts(countByStatus(allResults));
  const currentSuiteName = run.currentSuiteId ? suiteName(run.currentSuiteId) : null;

  return (
    <div className="runner-screen">
      <RunnerStatusBar
        phase={run.phase}
        currentSuiteName={currentSuiteName}
        completedSuiteCount={run.completedSuiteCount}
        totalSuiteCount={run.totalSuiteCount}
        tallies={tallies}
        durationMs={run.report?.duration_ms ?? 0}
        runError={run.runError}
        onViewReport={onViewReport}
      />

      <div className="runner-screen__body">
        <SuiteTree
          suiteIds={runSuiteIds}
          suiteName={suiteName}
          statuses={run.suiteStatuses}
          report={run.report}
          completedSuiteCount={run.completedSuiteCount}
          totalSuiteCount={run.totalSuiteCount}
          selectedSuiteId={selectedSuiteId}
          onSelect={setSelectedSuiteId}
        />
        <TestList
          results={scopedResults}
          suiteName={suiteName}
          failingOnly={failingOnly}
          onFailingOnlyChange={onFailingOnlyChange}
          totalFailingCount={tallies.fail}
        />
      </div>
    </div>
  );
}
