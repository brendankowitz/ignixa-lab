import { forwardRef, useRef, type CSSProperties, type Ref } from 'react';
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
  boxSizing: 'border-box',
  whiteSpace: 'pre',
};

/** An editable textarea with a live syntax-highlighted backdrop layered behind it, scroll-synced so the two stay pixel-aligned. Forwards a ref to the underlying `<textarea>` so callers can imperatively focus/select (e.g. highlighting a parse-tree node's source span). */
export const HighlightedTextarea = forwardRef(function HighlightedTextarea(
  { value, onChange, lines, style, spellCheck = false, autoGrowMaxHeight }: HighlightedTextareaProps,
  ref: Ref<HTMLTextAreaElement>,
) {
  const preRef = useRef<HTMLPreElement>(null);

  // `fontSize` is set explicitly on both layers below rather than left to
  // inherit from the wrapper: a <pre> is a normal inheriting block element,
  // but a <textarea> is a replaced form control that browsers don't reliably
  // cascade `font-size` into from an ancestor without an explicit rule, so the
  // two layers could silently render at different actual font sizes even
  // though only the <pre> visibly reflected the intended size. `lineHeight` is
  // likewise computed once as an integer pixel value (not a bare multiplier)
  // and applied identically to both — browsers can round a fractional
  // `line-height` (e.g. 11.5px * 1.55 = 17.825px) to a different device pixel
  // for a <pre> vs. a <textarea>, so the two layers' lines drift out of sync
  // by a fraction of a pixel per line — invisible until you select text and
  // see the native selection box (rendered against the invisible textarea's
  // real line positions) creep away from the visible highlighted text
  // underneath it.
  const fontSizePx = typeof style?.fontSize === 'number' ? style.fontSize : DEFAULT_FONT_SIZE_PX;
  const lineHeightPx = Math.round(fontSizePx * LINE_HEIGHT_MULTIPLIER);
  const layerStyle: CSSProperties = { ...layerBaseStyle, fontSize: fontSizePx, lineHeight: `${lineHeightPx}px` };

  const wrapperStyle = { ...wrapperBaseStyle, ...style };
  if (autoGrowMaxHeight) {
    const minHeight = typeof style?.height === 'number' ? style.height : 0;
    const contentHeight = lines.length * lineHeightPx + VERTICAL_PADDING_PX;
    wrapperStyle.height = Math.min(Math.max(contentHeight, minHeight), autoGrowMaxHeight);
    wrapperStyle.resize = 'none';
  }

  return (
    <div style={wrapperStyle}>
      <pre ref={preRef} aria-hidden="true" style={{ ...layerStyle, overflow: 'hidden' }}>
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
        ref={ref}
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
          ...layerStyle,
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
});
