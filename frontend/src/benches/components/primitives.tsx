import type { CSSProperties, ReactNode } from 'react';
import { cardStyle, errorBannerStyle, pillGroupStyle, pillItemStyle } from './styles';

/** A bordered, padded panel used for every card-shaped section across the bench screens. */
export function Card({ children, style }: { children: ReactNode; style?: CSSProperties }) {
  return <div style={style ? { ...cardStyle, ...style } : cardStyle}>{children}</div>;
}

/** Mono-font red banner for evaluation/parse errors. */
export function ErrorBanner({ message }: { message: string }) {
  return <div style={errorBannerStyle}>{message}</div>;
}

/** A small on/off switch. The caller renders any adjacent label; this is just the track + knob. */
export function Toggle({
  checked,
  onChange,
  ariaLabel,
}: {
  checked: boolean;
  onChange: (checked: boolean) => void;
  ariaLabel?: string;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={ariaLabel}
      onClick={() => onChange(!checked)}
      style={{
        width: 40,
        height: 22,
        borderRadius: 99,
        border: 'none',
        padding: 0,
        position: 'relative',
        cursor: 'pointer',
        flex: 'none',
        background: checked ? 'var(--accent)' : 'var(--border2)',
        transition: 'background .15s',
      }}
    >
      <span
        style={{
          position: 'absolute',
          top: 2,
          left: checked ? 20 : 2,
          width: 18,
          height: 18,
          borderRadius: '50%',
          background: '#fff',
          transition: 'left .15s',
          boxShadow: '0 1px 2px rgba(0,0,0,.2)',
        }}
      />
    </button>
  );
}

export interface PillItem<T extends string = string> {
  id: T;
  label: string;
  /** When true, the pill can't be selected — rendered dimmed and non-interactive. */
  disabled?: boolean;
  /** Tooltip shown on hover — most useful paired with `disabled` (e.g. "Not yet implemented"). */
  title?: string;
}

export interface PillsProps<T extends string> {
  items: PillItem<T>[];
  activeId: T;
  onChange: (id: T) => void;
}

/** Segmented pill tab group, used for the bench switcher and for tab/selector groups within individual benches (e.g. FHIR version, result tabs) — not every bench uses it. */
export function Pills<T extends string>({ items, activeId, onChange }: PillsProps<T>) {
  return (
    <div style={{ ...pillGroupStyle, minWidth: 0 }} role="tablist">
      {items.map((item) => (
        <span
          key={item.id}
          role="tab"
          tabIndex={item.disabled ? -1 : 0}
          aria-selected={item.id === activeId}
          aria-disabled={item.disabled}
          title={item.title}
          onClick={() => {
            if (!item.disabled) {
              onChange(item.id);
            }
          }}
          onKeyDown={(event) => {
            if (item.disabled) {
              return;
            }
            if (event.key === 'Enter') {
              onChange(item.id);
            } else if (event.key === ' ') {
              event.preventDefault();
              onChange(item.id);
            }
          }}
          style={{
            ...pillItemStyle(item.id === activeId),
            ...(item.disabled ? { opacity: 0.45, cursor: 'not-allowed' } : null),
          }}
        >
          {item.label}
        </span>
      ))}
    </div>
  );
}
