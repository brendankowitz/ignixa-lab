import type { CSSProperties } from 'react';

/** Font stack for every code/data element across the three bench screens. */
export const monoFont = "'IBM Plex Mono', monospace";

export const sectionLabelStyle: CSSProperties = {
  fontFamily: monoFont,
  fontSize: 9.5,
  letterSpacing: '.14em',
  color: 'var(--text3)',
  textTransform: 'uppercase',
};

export const cardStyle: CSSProperties = {
  background: 'var(--panel)',
  border: '1px solid var(--border)',
  borderRadius: 12,
  padding: '14px 16px',
  display: 'flex',
  flexDirection: 'column',
  gap: 10,
};

export const monoTextareaStyle: CSSProperties = {
  width: '100%',
  boxSizing: 'border-box',
  border: '1px solid var(--border2)',
  borderRadius: 8,
  padding: '11px 13px',
  fontFamily: monoFont,
  fontSize: 12.5,
  lineHeight: 1.55,
  color: 'var(--text)',
  background: 'var(--code)',
  resize: 'vertical',
};

export const monoInputStyle: CSSProperties = {
  border: '1px solid var(--border2)',
  borderRadius: 8,
  padding: '9px 12px',
  fontFamily: monoFont,
  fontSize: 12.5,
  color: 'var(--text)',
  background: 'var(--panel)',
};

export const errorBannerStyle: CSSProperties = {
  padding: '10px 14px',
  borderRadius: 8,
  background: 'var(--fail-bg)',
  border: '1px solid var(--fail-border)',
  fontFamily: monoFont,
  fontSize: 12,
  color: 'var(--fail)',
  lineHeight: 1.5,
};

export const pillGroupStyle: CSSProperties = {
  display: 'flex',
  gap: 2,
  background: 'var(--inset)',
  borderRadius: 8,
  padding: 3,
};

export function pillItemStyle(active: boolean): CSSProperties {
  return {
    padding: '6px 14px',
    borderRadius: 6,
    fontSize: 12.5,
    cursor: 'pointer',
    background: active ? 'var(--pill)' : 'transparent',
    color: active ? 'var(--text)' : 'var(--text3)',
    fontWeight: active ? 600 : 500,
    boxShadow: active ? '0 1px 3px var(--border2)' : 'none',
  };
}

export function chipStyle(bg: string, fg: string): CSSProperties {
  return {
    fontFamily: monoFont,
    fontSize: 9.5,
    fontWeight: 600,
    padding: '3px 8px',
    borderRadius: 99,
    background: bg,
    color: fg,
  };
}

export const primaryButtonStyle: CSSProperties = {
  padding: '8px 18px',
  borderRadius: 8,
  background: 'var(--accent)',
  color: 'var(--accent-contrast)',
  fontSize: 13,
  fontWeight: 600,
  cursor: 'pointer',
  border: 'none',
  boxShadow: 'var(--accent-shadow)',
};

export const engineBadgeStyle: CSSProperties = {
  fontFamily: monoFont,
  fontSize: 10,
  padding: '3px 9px',
  borderRadius: 99,
  background: 'var(--inset)',
  color: 'var(--text3)',
};
