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

export interface PillItem<T extends string = string> {
  id: T;
  label: string;
}

export interface PillsProps<T extends string> {
  items: PillItem<T>[];
  activeId: T;
  onChange: (id: T) => void;
}

/** Segmented pill tab group, used for both the bench switcher and each bench's result tabs. */
export function Pills<T extends string>({ items, activeId, onChange }: PillsProps<T>) {
  return (
    <div style={pillGroupStyle} role="tablist">
      {items.map((item) => (
        <span
          key={item.id}
          role="tab"
          tabIndex={0}
          aria-selected={item.id === activeId}
          onClick={() => onChange(item.id)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              onChange(item.id);
            } else if (event.key === ' ') {
              event.preventDefault();
              onChange(item.id);
            }
          }}
          style={pillItemStyle(item.id === activeId)}
        >
          {item.label}
        </span>
      ))}
    </div>
  );
}
