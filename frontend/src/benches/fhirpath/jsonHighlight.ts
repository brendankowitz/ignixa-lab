import { tokenizeJson, type JsonTokenClass } from '../../lib/httpFormat';
import type { HighlightLine } from '../components/HighlightedTextarea';

function tokenColor(cls: JsonTokenClass): string {
  switch (cls) {
    case 'key':
      return 'var(--tok-key)';
    case 'string':
      return 'var(--tok-string)';
    case 'number':
      return 'var(--tok-number)';
    case 'boolean':
      return 'var(--tok-boolean)';
    case 'null':
      return 'var(--tok-null)';
    case 'punct':
      return 'var(--tok-punct)';
    default:
      return 'var(--tok-plain)';
  }
}

/** Adapts the flat token stream from {@link tokenizeJson} into per-line segments for {@link HighlightedTextarea}. */
export function highlightJson(text: string): HighlightLine[] {
  const tokens = tokenizeJson(text);
  const lines: HighlightLine[] = [{ segments: [] }];

  for (const token of tokens) {
    const color = tokenColor(token.cls);
    const parts = token.value.split('\n');
    parts.forEach((part, index) => {
      if (index > 0) {
        lines.push({ segments: [] });
      }
      if (part.length > 0) {
        lines[lines.length - 1].segments.push({ text: part, color });
      }
    });
  }

  // A blank line has no segments, which HighlightedTextarea would render as an
  // empty, zero-height <div> — collapsing it and misaligning every line below
  // against the textarea's full-height blank line. Give it a space so it takes
  // up a line's worth of height like its non-blank neighbors.
  for (const line of lines) {
    if (line.segments.length === 0) {
      line.segments.push({ text: ' ', color: 'var(--tok-plain)' });
    }
  }

  return lines;
}
