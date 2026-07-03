import { useState, type CSSProperties } from 'react';
import { useTheme } from '../hooks/useTheme';
import { Pills, type PillItem } from './components/primitives';
import { monoFont } from './components/styles';
import { FhirPathBench } from './fhirpath/FhirPathBench';
import { FmlBench } from './fml/FmlBench';
import { SofBench } from './sof/SofBench';
import { FakesBench } from './fakes/FakesBench';

type BenchId = 'fhirpath' | 'fml' | 'sqlonfhir' | 'fakes';

const BENCH_TABS: PillItem<BenchId>[] = [
  { id: 'fhirpath', label: 'FHIRPath' },
  { id: 'fml', label: 'FML' },
  { id: 'sqlonfhir', label: 'SQL on FHIR' },
  { id: 'fakes', label: 'Fakes' },
];

const shellStyle: CSSProperties = {
  minHeight: '100vh',
  background: 'var(--bg)',
  color: 'var(--text)',
  fontFamily: "'IBM Plex Sans', system-ui, sans-serif",
};

const topBarStyle: CSSProperties = {
  display: 'flex',
  flexWrap: 'wrap',
  alignItems: 'center',
  gap: 14,
  padding: '12px 20px',
  background: 'var(--panel)',
  borderBottom: '1px solid var(--border)',
  position: 'sticky',
  top: 0,
  zIndex: 20,
};

/** Top-level shell for the Expression Benches page: top bar + bench-switching tabs. No router — tab state is component-local, same convention as the conformance app's `App.tsx`. */
export function BenchesApp() {
  const theme = useTheme();
  const [bench, setBench] = useState<BenchId>('fhirpath');
  const [fakesReturnTo, setFakesReturnTo] = useState<Exclude<BenchId, 'fakes'> | null>(null);
  const [sentToast, setSentToast] = useState<{ bench: BenchId; label: string } | null>(null);
  const [fhirpathSeed, setFhirpathSeed] = useState<{ text: string } | null>(null);
  const [fmlSeed, setFmlSeed] = useState<{ text: string } | null>(null);
  const [sofSeed, setSofSeed] = useState<{ text: string } | null>(null);

  const openFakesFrom = (fromBench: Exclude<BenchId, 'fakes'>) => {
    setBench('fakes');
    setFakesReturnTo(fromBench);
  };

  const handleSend = (targetBench: 'fhirpath' | 'fml' | 'sqlonfhir', payload: Record<string, unknown> | Record<string, unknown>[], label: string) => {
    const text = JSON.stringify(payload, null, 2);
    if (targetBench === 'fhirpath') {
      setFhirpathSeed({ text });
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
      <header style={topBarStyle}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 9 }}>
          <div aria-hidden="true" style={{ width: 30, height: 30, borderRadius: 8, background: 'var(--grad)', flex: 'none' }} />
          <div style={{ display: 'flex', flexDirection: 'column' }}>
            <span style={{ fontSize: 14.5, fontWeight: 700, letterSpacing: '-.01em' }}>Ignixa Lab</span>
            <span style={{ fontFamily: monoFont, fontSize: 9, letterSpacing: '.14em', color: 'var(--text3)', textTransform: 'uppercase' }}>
              Expression benches
            </span>
          </div>
        </div>

        <div style={{ marginLeft: 20 }}>
          <Pills items={BENCH_TABS} activeId={bench} onChange={setBench} />
        </div>

        <a href="./" style={{ fontSize: 12, fontWeight: 600, color: 'var(--text3)', textDecoration: 'none', padding: '6px 10px' }}>
          Conformance ↗
        </a>

        <div style={{ flex: 1 }} />

        <span style={{ fontFamily: monoFont, fontSize: 11, color: 'var(--text3)' }}>
          {bench === 'fhirpath' ? 'live engine' : 'mock engine · exploration'}
        </span>

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
        {bench === 'fhirpath' ? <FhirPathBench onOpenFakes={() => openFakesFrom('fhirpath')} fakesSeed={fhirpathSeed} onSeedConsumed={() => setFhirpathSeed(null)} /> : null}
        {bench === 'fml' ? <FmlBench onOpenFakes={() => openFakesFrom('fml')} fakesSeed={fmlSeed} onSeedConsumed={() => setFmlSeed(null)} /> : null}
        {bench === 'sqlonfhir' ? <SofBench onOpenFakes={() => openFakesFrom('sqlonfhir')} fakesSeed={sofSeed} onSeedConsumed={() => setSofSeed(null)} /> : null}
        {bench === 'fakes' ? (
          <FakesBench
            returnTo={fakesReturnTo}
            onDismissReturn={() => setFakesReturnTo(null)}
            onSend={(targetBench, payload, label) => handleSend(targetBench, payload, label)}
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
