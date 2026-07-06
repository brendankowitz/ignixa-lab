import { useEffect, useMemo, useState } from 'react';
import { ApiError, getCapability } from '../api/client';
import { countByStatus, formatDuration, groupBySuite, passRate, rollupCounts } from '../lib/conformance';
import { mergeCoverage, observedCoverage } from '../lib/coverage';
import { downloadTestReportBundle } from '../lib/testReport';
import type { CapabilitySummary } from '../types/capability';
import type { ConformanceReport, SuiteDescriptor } from '../types/conformance';
import { CoverageMap } from './CoverageMap';

/** Props for {@link ReportScreen}. */
export interface ReportScreenProps {
  report: ConformanceReport | null;
  suites: SuiteDescriptor[];
  /** Commit the bundled testscripts came from, used to link each downloaded TestReport's `testScript` back to its GitHub source. */
  testScriptsRevision: string | null;
  onViewFailing: () => void;
}

/**
 * Post-run report: headline pass rate, per-suite pass bars, and the
 * capability coverage map (declared capabilities merged with what this run
 * actually observed). The coverage fetch degrades gracefully — a failed
 * `GET /api/capability` (e.g. the target exposes no `/metadata`) still
 * renders observed-only coverage rather than blocking the screen.
 */
export function ReportScreen({ report, suites, testScriptsRevision, onViewFailing }: ReportScreenProps) {
  const suiteById = useMemo(() => new Map(suites.map((suite) => [suite.id, suite])), [suites]);

  const [capability, setCapability] = useState<CapabilitySummary | null>(null);
  const [capabilityError, setCapabilityError] = useState<string | null>(null);
  const [capabilityLoading, setCapabilityLoading] = useState(false);

  useEffect(() => {
    if (!report) {
      return;
    }
    const abort = new AbortController();
    setCapability(null);
    setCapabilityError(null);
    setCapabilityLoading(true);
    getCapability(report.target, report.fhirVersion, abort.signal)
      .then((summary) => setCapability(summary))
      .catch((error: unknown) => {
        if (!abort.signal.aborted) {
          setCapabilityError(describeError(error));
        }
      })
      .finally(() => {
        if (!abort.signal.aborted) {
          setCapabilityLoading(false);
        }
      });
    return () => abort.abort();
  }, [report]);

  if (!report) {
    return (
      <div className="report-screen report-screen--empty">
        <p>Run tests to see a report.</p>
      </div>
    );
  }

  const counts = countByStatus(report.results);
  const tallies = rollupCounts(counts);
  const overallPct = Math.round(passRate(counts) * 100);
  const suiteGroups = groupBySuite(report);
  const coverageRows = mergeCoverage(capability, observedCoverage(report));

  return (
    <div className="report-screen">
      <div className="report-header">
        <div className="report-header__rate">
          <span className="report-header__rate-value">{overallPct}%</span>
          <span className="report-header__rate-label">overall conformance</span>
        </div>
        <div className="report-header__divider" />
        <div className="report-header__pills">
          <span className="report-pill report-pill--pass">{tallies.pass} PASS</span>
          <span className="report-pill report-pill--fail">{tallies.fail} FAIL</span>
          <span className="report-pill report-pill--skip">{tallies.skipped} SKIPPED</span>
        </div>
        <div className="report-header__spacer" />
        <span className="report-header__meta">
          {report.target} · {report.fhirVersion} · {formatDuration(report.duration_ms)}
        </span>
        <button
          type="button"
          className="report-header__download"
          onClick={() => downloadTestReportBundle(report, testScriptsRevision)}
        >
          Download TestReport
        </button>
        <button type="button" className="report-header__view-failing" onClick={onViewFailing}>
          View {tallies.fail} failing →
        </button>
      </div>

      <section className="report-panel" aria-label="Suites">
        <span className="report-panel__title">Suites</span>
        <div className="suite-bars">
          {suiteGroups.map((group) => {
            const pct = Math.round(passRate(group.counts) * 100);
            const rolled = rollupCounts(group.counts);
            const total = rolled.pass + rolled.fail + rolled.skipped;
            return (
              <div key={group.suite} className="suite-bar">
                <div className="suite-bar__header">
                  <span className="suite-bar__name">{suiteById.get(group.suite)?.name ?? group.suite}</span>
                  <span className="suite-bar__detail">
                    {rolled.pass} / {total} passed
                    {rolled.skipped > 0 ? ` · ${rolled.skipped} skipped` : ''}
                    {rolled.fail > 0 ? ` · ${rolled.fail} failed` : ''}
                  </span>
                  <span className={`suite-bar__pct${pct < 85 ? ' suite-bar__pct--low' : ''}`}>{pct}%</span>
                </div>
                <div className="suite-bar__track">
                  <div className="suite-bar__fill" style={{ width: `${pct}%` }} />
                </div>
              </div>
            );
          })}
        </div>
      </section>

      <section className="report-panel" aria-label="Capability coverage">
        <div className="report-panel__header">
          <span className="report-panel__title">Capability coverage</span>
          <span className="report-panel__subtitle">CapabilityStatement vs observed results</span>
        </div>
        {capabilityLoading ? <p className="report-panel__status">Loading declared capabilities…</p> : null}
        {capabilityError ? (
          <p className="report-panel__status report-panel__status--error">
            {capabilityError} — showing observed-only coverage.
          </p>
        ) : null}
        <CoverageMap rows={coverageRows} />
      </section>
    </div>
  );
}

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message;
  }
  if (error instanceof Error) {
    return error.message;
  }
  return 'Could not load declared capabilities.';
}
