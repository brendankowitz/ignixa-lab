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

export interface PillItem {
  id: string;
  label: string;
}

export interface PillsProps {
  items: PillItem[];
  activeId: string;
  onChange: (id: string) => void;
}

/** Segmented pill tab group, used for both the bench switcher and each bench's result tabs. */
export function Pills({ items, activeId, onChange }: PillsProps) {
  return (
    <div style={pillGroupStyle}>
      {items.map((item) => (
        <span key={item.id} onClick={() => onChange(item.id)} style={pillItemStyle(item.id === activeId)}>
          {item.label}
        </span>
      ))}
    </div>
  );
}
