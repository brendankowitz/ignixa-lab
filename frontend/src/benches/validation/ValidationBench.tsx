import { useEffect, useMemo, useState, type CSSProperties } from 'react';
import { useIsNarrowViewport } from '../../hooks/useIsNarrowViewport';
import type { ValidationShareState } from '../../lib/shareLinks';
import { HighlightedTextarea } from '../components/HighlightedTextarea';
import { highlightJson } from '../components/jsonHighlight';
import { Card, ErrorBanner, Pills, Toggle, type PillItem } from '../components/primitives';
import { chipStyle, engineBadgeStyle, monoFont, monoInputStyle, sectionLabelStyle } from '../components/styles';
import { DEFAULT_VALIDATION_RESOURCE, VALIDATION_SAMPLES, type ValidationSampleId } from './sampleResources';
import { parsePackageText, useValidationRun } from './useValidationRun';
import type { ValidationDepth, ValidationIssue, ValidationSeverity } from './validationTypes';

const VERSION_ITEMS: PillItem<string>[] = [
  { id: 'stu3', label: 'STU3' },
  { id: 'r4', label: 'R4' },
  { id: 'r4b', label: 'R4B' },
  { id: 'r5', label: 'R5' },
  { id: 'r6', label: 'R6' },
];

const DEPTH_ITEMS: PillItem<ValidationDepth>[] = [
  { id: 'minimal', label: 'Minimal' },
  { id: 'spec', label: 'Spec' },
  { id: 'full', label: 'Full' },
  { id: 'compatibility', label: 'Compatibility' },
];

const FILTER_ITEMS: PillItem<'all' | ValidationSeverity>[] = [
  { id: 'all', label: 'All' },
  { id: 'fatal', label: 'Fatal' },
  { id: 'error', label: 'Errors' },
  { id: 'warning', label: 'Warnings' },
  { id: 'information', label: 'Info' },
];

function severityColors(severity: ValidationSeverity): { bg: string; fg: string } {
  switch (severity) {
    case 'fatal':
    case 'error':
      return { bg: 'var(--fail-bg)', fg: 'var(--fail)' };
    case 'warning':
      return { bg: 'var(--chip-amb-bg)', fg: 'var(--chip-amb-fg)' };
    case 'information':
      return { bg: 'var(--chip-teal-bg)', fg: 'var(--chip-teal-fg)' };
  }
}

function IssueRow({ issue }: { issue: ValidationIssue }) {
  const colors = severityColors(issue.severity);
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6, padding: '10px 12px', borderRadius: 9, background: 'var(--code)', border: '1px solid var(--border)' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <span style={chipStyle(colors.bg, colors.fg)}>{issue.severity}</span>
        {issue.code ? <span style={chipStyle('var(--chip-gray-bg)', 'var(--chip-gray2-fg)')}>{issue.code}</span> : null}
        <span style={{ fontFamily: monoFont, fontSize: 11.5, color: 'var(--accent)' }}>{issue.path || '$'}</span>
      </div>
      <div style={{ fontSize: 12.5, color: 'var(--text)', lineHeight: 1.45 }}>{issue.message}</div>
      {issue.details && issue.details !== issue.message ? (
        <div style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text3)', lineHeight: 1.45 }}>{issue.details}</div>
      ) : null}
    </div>
  );
}

export interface ValidationBenchProps {
  onOpenFakes?: () => void;
  fakesSeed?: { text: string } | null;
  onSeedConsumed?: () => void;
  initialState?: ValidationShareState;
  onShareStateChange?: (state: ValidationShareState) => void;
}

export function ValidationBench({ onOpenFakes, fakesSeed, onSeedConsumed, initialState, onShareStateChange }: ValidationBenchProps) {
  const stacked = useIsNarrowViewport(840);
  const twoColumnStyle: CSSProperties = {
    display: 'grid',
    gridTemplateColumns: stacked ? '1fr' : 'minmax(360px,44%) 1fr',
    gap: 14,
    alignItems: 'start',
  };

  const [fhirVersion, setFhirVersion] = useState(initialState?.fhirVersion ?? 'r4');
  const [depth, setDepth] = useState<ValidationDepth>(initialState?.depth ?? 'spec');
  const [skipTerminology, setSkipTerminology] = useState(initialState?.skipTerminology ?? false);
  const [packageText, setPackageText] = useState(initialState?.packageText ?? '');
  const [sampleId, setSampleId] = useState<ValidationSampleId>(initialState?.sampleId ?? 'patient-invalid');
  const [resourceText, setResourceText] = useState(initialState?.resourceText ?? DEFAULT_VALIDATION_RESOURCE);
  const [filter, setFilter] = useState<'all' | ValidationSeverity>('all');

  useEffect(() => {
    onShareStateChange?.({ fhirVersion, depth, skipTerminology, packageText, sampleId, resourceText });
  }, [depth, fhirVersion, onShareStateChange, packageText, resourceText, sampleId, skipTerminology]);

  useEffect(() => {
    if (fakesSeed) {
      setSampleId('custom');
      setResourceText(fakesSeed.text);
      onSeedConsumed?.();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fakesSeed]);

  const resourceHighlight = useMemo(() => highlightJson(resourceText), [resourceText]);
  const run = useValidationRun({ fhirVersion, depth, skipTerminology, packageText, resourceText });
  const packageCount = parsePackageText(packageText).length;
  const issues = run.result?.issues ?? [];
  const filteredIssues = filter === 'all' ? issues : issues.filter((issue) => issue.severity === filter);

  return (
    <div style={{ maxWidth: 1280, margin: '0 auto', padding: '22px 24px 60px', display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 12, flexWrap: 'wrap' }}>
        <h1 style={{ margin: 0, fontSize: 21, fontWeight: 700, letterSpacing: '-.02em' }}>Resource validation</h1>
        <span style={{ fontSize: 12.5, color: 'var(--text3)' }}>
          Validate a resource with Ignixa.Validation — depth, terminology, and IG package layering.
        </span>
        <div style={{ flex: 1 }} />
        <span style={engineBadgeStyle}>{run.result ? `Ignixa.Validation ${run.result.engineVersion}` : run.isLoading ? 'validating…' : 'Ignixa.Validation'}</span>
      </div>

      <Card>
        <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', alignItems: 'center' }}>
          <span style={sectionLabelStyle}>FHIR version</span>
          <Pills items={VERSION_ITEMS} activeId={fhirVersion} onChange={setFhirVersion} />
          <span style={{ ...sectionLabelStyle, marginLeft: 8 }}>Depth</span>
          <Pills items={DEPTH_ITEMS} activeId={depth} onChange={setDepth} />
          <span style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, color: 'var(--text3)' }}>
            Skip terminology
            <Toggle checked={skipTerminology} onChange={setSkipTerminology} ariaLabel="Skip terminology validation" />
          </span>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span style={sectionLabelStyle}>
            IG packages <span style={{ textTransform: 'none', letterSpacing: 0, color: 'var(--text4)' }}>· optional id@version, comma or newline separated</span>
          </span>
          <input
            value={packageText}
            onChange={(event) => setPackageText(event.target.value)}
            spellCheck={false}
            placeholder="hl7.fhir.us.core@6.1.0"
            style={monoInputStyle}
          />
          {packageCount > 0 ? (
            <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text4)' }}>{packageCount} package{packageCount === 1 ? '' : 's'} will be layered like the CLI --package option.</span>
          ) : null}
        </div>
      </Card>

      <div style={twoColumnStyle}>
        <Card style={{ minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <span style={{ ...sectionLabelStyle, flex: 1 }}>Resource JSON</span>
            {VALIDATION_SAMPLES.map((sample) => (
              <button
                key={sample.id}
                type="button"
                onClick={() => {
                  setSampleId(sample.id);
                  setResourceText(JSON.stringify(sample.data, null, 2));
                }}
                style={{
                  font: 'inherit',
                  fontSize: 11,
                  fontWeight: 600,
                  padding: '4px 11px',
                  borderRadius: 99,
                  cursor: 'pointer',
                  background: sampleId === sample.id ? 'var(--chip-vio-bg)' : 'var(--panel)',
                  color: sampleId === sample.id ? 'var(--chip-vio-fg)' : 'var(--text3)',
                  border: `1px solid ${sampleId === sample.id ? 'var(--accent-border)' : 'var(--border2)'}`,
                }}
              >
                {sample.label}
              </button>
            ))}
            {onOpenFakes ? (
              <button
                type="button"
                onClick={onOpenFakes}
                title="Generate a test resource with Fakes"
                style={{
                  fontSize: 11,
                  fontWeight: 600,
                  padding: '4px 11px',
                  borderRadius: 99,
                  cursor: 'pointer',
                  background: 'var(--chip-vio-bg)',
                  color: 'var(--accent)',
                  border: '1px solid var(--accent-border)',
                }}
              >
                ⚡ Fakes ↗
              </button>
            ) : null}
          </div>
          <HighlightedTextarea value={resourceText} onChange={setResourceText} lines={resourceHighlight} style={{ minHeight: 560, fontSize: 11.5 }} />
        </Card>

        <Card style={{ minHeight: 440, minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <Pills items={FILTER_ITEMS} activeId={filter} onChange={setFilter} />
            <div style={{ flex: 1 }} />
            {run.isLoading ? <span style={{ fontFamily: monoFont, fontSize: 10.5, color: 'var(--text3)' }}>validating…</span> : null}
          </div>

          {run.error ? <ErrorBanner message={run.error} /> : null}

          {run.result && !run.error ? (
            <>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(94px, 1fr))', gap: 8 }}>
                {[
                  ['Fatal', run.result.summary.fatal, 'fatal'],
                  ['Errors', run.result.summary.error, 'error'],
                  ['Warnings', run.result.summary.warning, 'warning'],
                  ['Info', run.result.summary.information, 'information'],
                ].map(([label, count, severity]) => {
                  const colors = severityColors(severity as ValidationSeverity);
                  return (
                    <div key={label} style={{ padding: '10px 12px', borderRadius: 9, background: colors.bg, color: colors.fg }}>
                      <div style={{ fontFamily: monoFont, fontSize: 10, textTransform: 'uppercase', letterSpacing: '.12em' }}>{label}</div>
                      <div style={{ fontSize: 22, fontWeight: 700 }}>{count}</div>
                    </div>
                  );
                })}
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                <span style={chipStyle(run.result.isValid ? 'var(--pass-bg)' : 'var(--fail-bg)', run.result.isValid ? 'var(--pass)' : 'var(--fail)')}>
                  {run.result.isValid ? 'valid' : 'invalid'}
                </span>
                <span style={{ fontFamily: monoFont, fontSize: 11.5, color: 'var(--text3)' }}>
                  {run.result.resourceType} · {run.result.fhirVersion.toUpperCase()} · {run.result.depth}
                </span>
              </div>

              {filteredIssues.length > 0 ? (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  {filteredIssues.map((issue, index) => <IssueRow key={`${issue.path}-${issue.code}-${index}`} issue={issue} />)}
                </div>
              ) : (
                <div style={{ padding: '16px', borderRadius: 9, border: '1px dashed var(--border2)', color: 'var(--text4)', fontSize: 12.5 }}>
                  {issues.length === 0 ? 'No validation issues found.' : 'No issues match this filter.'}
                </div>
              )}
            </>
          ) : null}
        </Card>
      </div>
    </div>
  );
}
