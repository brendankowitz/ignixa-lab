import type { CSSProperties } from 'react';
import { useCopyToClipboard } from '../../hooks/useCopyToClipboard';
import { COPY_FEEDBACK_DURATION_MS } from '../../lib/shareLinks';
import { monoFont, sectionLabelStyle } from '../components/styles';

export interface ResourcePathStatusBarProps {
  path: string | null;
  invalid: boolean;
}

const iconStyle: CSSProperties = {
  width: 14,
  height: 14,
  display: 'block',
};

export function ResourcePathStatusBar({ path, invalid }: ResourcePathStatusBarProps) {
  const { copied, failed, copy } = useCopyToClipboard(path ?? '', COPY_FEEDBACK_DURATION_MS);

  return (
    <div
      style={{
        minHeight: 38,
        width: '100%',
        maxWidth: '100%',
        minWidth: 0,
        boxSizing: 'border-box',
        display: 'flex',
        alignItems: 'center',
        gap: 9,
        padding: '5px 7px 5px 12px',
        border: '1px solid var(--border2)',
        borderTop: 'none',
        borderRadius: '0 0 8px 8px',
        background: 'var(--inset)',
        overflow: 'hidden',
      }}
    >
      <span style={{ ...sectionLabelStyle, flex: '0 0 auto' }}>Path</span>
      <span
        title={path ?? undefined}
        style={{
          minWidth: 0,
          flex: '1 1 auto',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          userSelect: path ? 'text' : undefined,
          cursor: path ? 'text' : 'default',
          fontFamily: monoFont,
          fontSize: 11.5,
          color: invalid ? 'var(--fail)' : path ? 'var(--text)' : 'var(--text4)',
        }}
      >
        {invalid ? 'Fix JSON to inspect a path' : path ?? 'Click a JSON key or value'}
      </span>
      <span
        role="status"
        aria-live="polite"
        style={{
          flex: '0 0 auto',
          fontFamily: monoFont,
          fontSize: 10.5,
          color: failed ? 'var(--fail)' : 'var(--pass)',
        }}
      >
        {failed ? 'Copy failed' : copied ? 'Copied' : ''}
      </span>
      <button
        type="button"
        aria-label="Copy FHIRPath"
        title="Copy FHIRPath"
        disabled={!path}
        onClick={copy}
        style={{
          flex: '0 0 auto',
          width: 26,
          height: 26,
          padding: 0,
          display: 'grid',
          placeItems: 'center',
          borderRadius: 6,
          border: '1px solid var(--border2)',
          background: 'var(--panel)',
          color: copied ? 'var(--pass)' : 'var(--text3)',
          cursor: path ? 'pointer' : 'default',
          opacity: path ? 1 : 0.45,
        }}
      >
        {copied ? (
          <svg viewBox="0 0 16 16" aria-hidden="true" style={iconStyle}>
            <path d="M3 8.5 6.25 12 13 4.5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        ) : (
          <svg viewBox="0 0 16 16" aria-hidden="true" style={iconStyle}>
            <rect x="5.25" y="5.25" width="7.5" height="8" rx="1.25" fill="none" stroke="currentColor" strokeWidth="1.25" />
            <path d="M10.75 5.25V3.5c0-.69-.56-1.25-1.25-1.25h-6c-.69 0-1.25.56-1.25 1.25v6c0 .69.56 1.25 1.25 1.25h1.75" fill="none" stroke="currentColor" strokeWidth="1.25" />
          </svg>
        )}
      </button>
    </div>
  );
}
