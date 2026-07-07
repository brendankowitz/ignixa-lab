import { useMemo, useState, type CSSProperties } from 'react';
import { useCopyToClipboard } from '../hooks/useCopyToClipboard';
import { useIsNarrowViewport } from '../hooks/useIsNarrowViewport';
import { useTheme } from '../hooks/useTheme';
import { COPY_FEEDBACK_DURATION_MS, buildBenchShareUrl, readBenchShare, type BenchShareState, type FakesShareState, type FhirPathShareState, type ValidationShareState } from '../lib/shareLinks';
import { Pills, type PillItem } from './components/primitives';
import { monoFont } from './components/styles';
import { FhirPathBench } from './fhirpath/FhirPathBench';
import { FmlBench } from './fml/FmlBench';
import { SofBench } from './sof/SofBench';
import { FakesBench } from './fakes/FakesBench';
import { ValidationBench } from './validation/ValidationBench';

type BenchId = 'fhirpath' | 'validation' | 'fml' | 'sqlonfhir' | 'fakes';

const BENCH_TABS: PillItem<BenchId>[] = [
  { id: 'fhirpath', label: 'FHIRPath' },
  { id: 'validation', label: 'Validation' },
  { id: 'fakes', label: 'Fakes' },
  { id: 'sqlonfhir', label: 'SQL on FHIR', disabled: true, title: 'Not yet implemented' },
  { id: 'fml', label: 'FML', disabled: true, title: 'Not yet implemented' },
];

const shellStyle: CSSProperties = {
  minHeight: '100vh',
  background: 'var(--bg)',
  color: 'var(--text)',
  fontFamily: "'IBM Plex Sans', system-ui, sans-serif",
};

function topBarStyle(compact: boolean): CSSProperties {
  return {
  display: 'flex',
  flexWrap: 'wrap',
  alignItems: 'center',
    gap: compact ? 8 : 14,
    padding: compact ? '10px 12px' : '12px 20px',
  background: 'var(--panel)',
  borderBottom: '1px solid var(--border)',
  position: 'sticky',
  top: 0,
  zIndex: 20,
  };
}

/** Top-level shell for the Expression Benches page: top bar + bench-switching tabs. No router — tab state is component-local, same convention as the conformance app's `App.tsx`. */
export function BenchesApp() {
  const initialLink = useMemo(readBenchShare, []);
  const theme = useTheme();
  const compactHeader = useIsNarrowViewport(680);
  const [bench, setBench] = useState<BenchId>(initialLink.bench ?? 'fhirpath');
  const [fakesReturnTo, setFakesReturnTo] = useState<Exclude<BenchId, 'fakes'> | null>(null);
  const [sentToast, setSentToast] = useState<{ bench: BenchId; label: string } | null>(null);
  const [fhirpathSeed, setFhirpathSeed] = useState<{ text: string } | null>(null);
  const [validationSeed, setValidationSeed] = useState<{ text: string } | null>(null);
  const [fmlSeed, setFmlSeed] = useState<{ text: string } | null>(null);
  const [sofSeed, setSofSeed] = useState<{ text: string } | null>(null);
  const [fhirpathShare, setFhirpathShare] = useState<FhirPathShareState | undefined>(initialLink.state.fhirpath);
  const [validationShare, setValidationShare] = useState<ValidationShareState | undefined>(initialLink.state.validation);
  const [fakesShare, setFakesShare] = useState<FakesShareState | undefined>(initialLink.state.fakes);

  const shareUrl = useMemo(() => {
    const shareState: BenchShareState = { fhirpath: fhirpathShare, validation: validationShare, fakes: fakesShare };
    return buildBenchShareUrl(bench, shareState);
  }, [bench, fhirpathShare, validationShare, fakesShare]);

  const { copied, copy: copyShareLink } = useCopyToClipboard(shareUrl, COPY_FEEDBACK_DURATION_MS);

  const openFakesFrom = (fromBench: Exclude<BenchId, 'fakes'>) => {
    setBench('fakes');
    setFakesReturnTo(fromBench);
  };

  const handleSend = (targetBench: 'fhirpath' | 'validation' | 'fml' | 'sqlonfhir', payload: Record<string, unknown> | Record<string, unknown>[], label: string) => {
    const text = JSON.stringify(payload, null, 2);
    if (targetBench === 'fhirpath') {
      setFhirpathSeed({ text });
    } else if (targetBench === 'validation') {
      setValidationSeed({ text });
    } else if (targetBench === 'fml') {
      setFmlSeed({ text });
    } else {
      setSofSeed({ text });
    }

    setBench(targetBench);
    setFakesReturnTo(null);
    setSentToast({ bench: targetBench, label });
    setTimeout(() => setSentToast(null), 6000);
  };

  return (
    <div style={{ ...shellStyle, ...(theme.variables as CSSProperties) }}>
      <header style={topBarStyle(compactHeader)}>
        <a href="./" aria-label="Ignixa home" style={{ display: 'flex', alignItems: 'center', gap: 9, textDecoration: 'none', color: 'inherit' }}>
          <div aria-hidden="true" style={{ width: 30, height: 30, borderRadius: 8, background: 'var(--grad)', flex: 'none' }} />
          <div style={{ display: 'flex', flexDirection: 'column' }}>
            <span style={{ fontSize: 14.5, fontWeight: 700, letterSpacing: '-.01em' }}>Ignixa</span>
            <span style={{ fontFamily: monoFont, fontSize: 9, letterSpacing: '.14em', color: 'var(--text3)', textTransform: 'uppercase' }}>
              FHIR toolkit
            </span>
          </div>
        </a>

        <div className="ix-app-switch">
          <a href="./conformance.html" className="ix-app-switch__item">
            <span style={{ fontSize: 9 }}>▶</span>Conformance
          </a>
          <span className="ix-app-switch__item ix-app-switch__item--active">
            <span style={{ fontSize: 11 }}>ƒ</span>Benches
          </span>
        </div>
        <div className="ix-top-divider" />

        <Pills items={BENCH_TABS} activeId={bench} onChange={setBench} />

        <div style={{ flex: compactHeader ? '0 0 0' : 1, display: compactHeader ? 'none' : 'block' }} />

        <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text3)', display: compactHeader ? 'none' : 'inline' }}>
          {bench === 'fhirpath' || bench === 'validation' || bench === 'fakes' ? 'live engine' : 'mock engine · exploration'}
        </span>

        <button
          type="button"
          onClick={copyShareLink}
          title="Copy share link"
          style={{
            width: 32,
            height: 32,
            display: 'grid',
            placeItems: 'center',
            border: '1px solid var(--border2)',
            borderRadius: 8,
            background: 'transparent',
            color: 'var(--text3)',
            fontSize: 14,
            cursor: 'pointer',
          }}
        >
          {copied ? '✓' : '🔗'}
        </button>

        <button
          type="button"
          onClick={theme.toggle}
          title="Toggle theme"
          style={{
            width: 32,
            height: 32,
            display: 'grid',
            placeItems: 'center',
            border: '1px solid var(--border2)',
            borderRadius: 8,
            background: 'transparent',
            color: 'var(--text3)',
            fontSize: 14,
            cursor: 'pointer',
          }}
        >
          {theme.icon}
        </button>
      </header>

      <main>
        {bench === 'fhirpath' ? <FhirPathBench onOpenFakes={() => openFakesFrom('fhirpath')} fakesSeed={fhirpathSeed} onSeedConsumed={() => setFhirpathSeed(null)} initialState={fhirpathShare} onShareStateChange={setFhirpathShare} /> : null}
        {bench === 'validation' ? <ValidationBench onOpenFakes={() => openFakesFrom('validation')} fakesSeed={validationSeed} onSeedConsumed={() => setValidationSeed(null)} initialState={validationShare} onShareStateChange={setValidationShare} /> : null}
        {bench === 'fml' ? <FmlBench onOpenFakes={() => openFakesFrom('fml')} fakesSeed={fmlSeed} onSeedConsumed={() => setFmlSeed(null)} /> : null}
        {bench === 'sqlonfhir' ? <SofBench onOpenFakes={() => openFakesFrom('sqlonfhir')} fakesSeed={sofSeed} onSeedConsumed={() => setSofSeed(null)} /> : null}
        {bench === 'fakes' ? (
          <FakesBench
            returnTo={fakesReturnTo}
            onDismissReturn={() => setFakesReturnTo(null)}
            onSend={(targetBench, payload, label) => handleSend(targetBench, payload, label)}
            initialState={fakesShare}
            onShareStateChange={setFakesShare}
          />
        ) : null}
      </main>

      {sentToast ? (
        <div
          style={{
            position: 'fixed',
            bottom: 22,
            left: '50%',
            transform: 'translateX(-50%)',
            zIndex: 40,
            display: 'flex',
            alignItems: 'center',
            gap: 10,
            padding: '11px 18px',
            borderRadius: 99,
            background: 'var(--text)',
            color: 'var(--bg)',
            fontSize: 12.5,
            fontWeight: 600,
            boxShadow: '0 8px 24px rgba(0,0,0,.22)',
          }}
        >
          <span style={{ color: '#4ade80' }}>✓</span> Received {sentToast.label}
        </div>
      ) : null}
    </div>
  );
}
