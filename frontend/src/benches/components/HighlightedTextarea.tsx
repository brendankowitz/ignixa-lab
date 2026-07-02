import { useRef, type CSSProperties } from 'react';
import { monoFont } from './styles';

export interface HighlightSegment {
  text: string;
  color: string;
}

export interface HighlightLine {
  segments: HighlightSegment[];
}

export interface HighlightedTextareaProps {
  value: string;
  onChange: (value: string) => void;
  lines: HighlightLine[];
  style?: CSSProperties;
  spellCheck?: boolean;
}

const wrapperBaseStyle: CSSProperties = {
  position: 'relative',
  border: '1px solid var(--border2)',
  borderRadius: 8,
  background: 'var(--code)',
  resize: 'vertical',
  overflow: 'hidden',
};

const layerBaseStyle: CSSProperties = {
  position: 'absolute',
  inset: 0,
  margin: 0,
  padding: '11px 13px',
  fontFamily: monoFont,
  lineHeight: 1.55,
  boxSizing: 'border-box',
  whiteSpace: 'pre',
};

/** An editable textarea with a live syntax-highlighted backdrop layered behind it, scroll-synced so the two stay pixel-aligned. */
export function HighlightedTextarea({ value, onChange, lines, style, spellCheck = false }: HighlightedTextareaProps) {
  const preRef = useRef<HTMLPreElement>(null);

  return (
    <div style={{ ...wrapperBaseStyle, ...style }}>
      <pre ref={preRef} aria-hidden="true" style={{ ...layerBaseStyle, overflow: 'hidden' }}>
        {lines.map((line, index) => (
          <div key={index}>
            {line.segments.map((segment, segmentIndex) => (
              <span key={segmentIndex} style={{ color: segment.color }}>
                {segment.text}
              </span>
            ))}
          </div>
        ))}
      </pre>
      <textarea
        value={value}
        onChange={(event) => onChange(event.target.value)}
        onScroll={(event) => {
          if (preRef.current) {
            preRef.current.scrollTop = event.currentTarget.scrollTop;
            preRef.current.scrollLeft = event.currentTarget.scrollLeft;
          }
        }}
        spellCheck={spellCheck}
        wrap="off"
        style={{
          ...layerBaseStyle,
          width: '100%',
          height: '100%',
          border: 'none',
          color: 'transparent',
          caretColor: 'var(--text)',
          background: 'transparent',
          resize: 'none',
          overflow: 'auto',
        }}
      />
    </div>
  );
}
