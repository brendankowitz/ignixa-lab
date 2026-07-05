import { useEffect, useRef, useState } from 'react';
import type { ThemeState } from '../hooks/useTheme';

/** The three top-level screens. Tab state is component-local — no router. */
export type TabId = 'setup' | 'runner' | 'report';

const TABS: { id: TabId; label: string }[] = [
  { id: 'setup', label: 'Setup' },
  { id: 'runner', label: 'Runner' },
  { id: 'report', label: 'Report' },
];

/** Props for {@link TopBar}. */
export interface TopBarProps {
  activeTab: TabId;
  onTabChange: (tab: TabId) => void;
  /** Bare target host + path (no protocol), or empty if none configured yet. */
  serverHost: string;
  fhirVersion: string;
  theme: ThemeState;
  running: boolean;
  /** Whether a run can be started right now (target set, at least one suite selected). */
  canStart: boolean;
  onStart: () => void;
  onStop: () => void;
  shareUrl: string;
}

/**
 * Sticky top bar: logo, tab navigation, `host · version` readout, theme
 * toggle, and the primary run/stop action — present on every screen.
 */
export function TopBar({
  activeTab,
  onTabChange,
  serverHost,
  fhirVersion,
  theme,
  running,
  canStart,
  onStart,
  onStop,
  shareUrl,
}: TopBarProps) {
  const [copied, setCopied] = useState(false);
  const copiedTimeout = useRef<number | null>(null);

  useEffect(() => {
    return () => {
      if (copiedTimeout.current !== null) {
        window.clearTimeout(copiedTimeout.current);
      }
    };
  }, []);

  const copyShareLink = () => {
    navigator.clipboard?.writeText(shareUrl).then(
      () => {
        setCopied(true);
        if (copiedTimeout.current !== null) {
          window.clearTimeout(copiedTimeout.current);
        }
        copiedTimeout.current = window.setTimeout(() => setCopied(false), 1400);
      },
      () => undefined,
    );
  };

  return (
    <header className="top-bar">
      <div className="top-bar__brand">
        <div className="top-bar__logo" aria-hidden="true" />
        <div className="top-bar__brand-text">
          <span className="top-bar__title">Ignixa Lab</span>
          <span className="top-bar__subtitle">Conformance</span>
        </div>
      </div>

      <nav className="top-bar__nav" aria-label="Screens">
        {TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            className={`top-bar__nav-item${activeTab === tab.id ? ' top-bar__nav-item--active' : ''}`}
            onClick={() => onTabChange(tab.id)}
          >
            {tab.label}
          </button>
        ))}
      </nav>

      <a href="benches.html" className="top-bar__nav-item top-bar__nav-item--plain">
        Expression Benches ↗
      </a>

      <div className="top-bar__spacer" />

      <span className="top-bar__readout">
        {serverHost || 'no target'} · {fhirVersion}
      </span>

      <button
        type="button"
        className="top-bar__theme-toggle"
        title="Copy share link"
        onClick={copyShareLink}
      >
        {copied ? '✓' : '🔗'}
      </button>

      <button
        type="button"
        className="top-bar__theme-toggle"
        title="Toggle theme"
        onClick={theme.toggle}
      >
        {theme.icon}
      </button>

      {running ? (
        <button type="button" className="top-bar__run-button top-bar__run-button--stop" onClick={onStop}>
          ■ Stop run
        </button>
      ) : (
        <button
          type="button"
          className="top-bar__run-button"
          onClick={onStart}
          disabled={!canStart}
        >
          ▶ Run tests
        </button>
      )}
    </header>
  );
}
