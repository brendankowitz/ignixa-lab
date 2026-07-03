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
  /**
   * When set, the box grows with `lines` — starting from `style.height` as a
   * floor — up to this height in px, then scrolls instead of growing further.
   * Height is derived from line count rather than measured DOM scrollHeight,
   * since the textarea is one of two absolutely-positioned layers here.
   */
  autoGrowMaxHeight?: number;
}

const wrapperBaseStyle: CSSProperties = {
  position: 'relative',
  border: '1px solid var(--border2)',
  borderRadius: 8,
  background: 'var(--code)',
  resize: 'vertical',
  overflow: 'hidden',
};

const VERTICAL_PADDING_PX = 22; // 11px top + 11px bottom, must match layerBaseStyle.padding below
const LINE_HEIGHT_MULTIPLIER = 1.55;
const DEFAULT_FONT_SIZE_PX = 13;

const layerBaseStyle: CSSProperties = {
  position: 'absolute',
  inset: 0,
  margin: 0,
  padding: '11px 13px',
  fontFamily: monoFont,
  lineHeight: LINE_HEIGHT_MULTIPLIER,
  boxSizing: 'border-box',
  whiteSpace: 'pre',
};

/** An editable textarea with a live syntax-highlighted backdrop layered behind it, scroll-synced so the two stay pixel-aligned. */
export function HighlightedTextarea({ value, onChange, lines, style, spellCheck = false, autoGrowMaxHeight }: HighlightedTextareaProps) {
  const preRef = useRef<HTMLPreElement>(null);

  const wrapperStyle = { ...wrapperBaseStyle, ...style };
  if (autoGrowMaxHeight) {
    const fontSizePx = typeof style?.fontSize === 'number' ? style.fontSize : DEFAULT_FONT_SIZE_PX;
    const minHeight = typeof style?.height === 'number' ? style.height : 0;
    const contentHeight = lines.length * fontSizePx * LINE_HEIGHT_MULTIPLIER + VERTICAL_PADDING_PX;
    wrapperStyle.height = Math.min(Math.max(contentHeight, minHeight), autoGrowMaxHeight);
    wrapperStyle.resize = 'none';
  }

  return (
    <div style={wrapperStyle}>
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
