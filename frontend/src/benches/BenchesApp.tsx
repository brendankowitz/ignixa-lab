import { useState, type CSSProperties } from 'react';
import { useTheme } from '../hooks/useTheme';
import { Pills } from './components/primitives';
import { monoFont } from './components/styles';
import { FhirPathBench } from './fhirpath/FhirPathBench';
import { FmlBench } from './fml/FmlBench';
import { SofBench } from './sof/SofBench';

type BenchId = 'fhirpath' | 'fml' | 'sqlonfhir';

const BENCH_TABS: { id: BenchId; label: string }[] = [
  { id: 'fhirpath', label: 'FHIRPath' },
  { id: 'fml', label: 'FML' },
  { id: 'sqlonfhir', label: 'SQL on FHIR' },
];

const shellStyle: CSSProperties = {
  minHeight: '100vh',
  background: 'var(--bg)',
  color: 'var(--text)',
  fontFamily: "'IBM Plex Sans', system-ui, sans-serif",
};

const topBarStyle: CSSProperties = {
  display: 'flex',
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

  return (
    <div style={{ ...shellStyle, ...(theme.variables as CSSProperties) }}>
      <header style={topBarStyle}>
        <div style={{ display: 'flex', flexDirection: 'column' }}>
          <span style={{ fontSize: 14.5, fontWeight: 700, letterSpacing: '-.01em' }}>Ignixa Lab</span>
          <span style={{ fontFamily: monoFont, fontSize: 9, letterSpacing: '.14em', color: 'var(--text3)', textTransform: 'uppercase' }}>
            Expression benches
          </span>
        </div>

        <div style={{ marginLeft: 20 }}>
          <Pills items={BENCH_TABS} activeId={bench} onChange={(id) => setBench(id as BenchId)} />
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
        {bench === 'fhirpath' ? <FhirPathBench /> : null}
        {bench === 'fml' ? <FmlBench /> : null}
        {bench === 'sqlonfhir' ? <SofBench /> : null}
      </main>
    </div>
  );
}
