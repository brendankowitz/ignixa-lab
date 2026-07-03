import type { CSSProperties } from 'react';
import { highlightJson } from './jsonHighlight';

/** Read-only syntax-highlighted JSON display — same token colors as {@link HighlightedTextarea}, without the editable overlay. */
export function HighlightedJsonBlock({ text, style }: { text: string; style?: CSSProperties }) {
  const lines = highlightJson(text);
  return (
    <pre style={{ ...style, margin: 0 }}>
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
  );
}
