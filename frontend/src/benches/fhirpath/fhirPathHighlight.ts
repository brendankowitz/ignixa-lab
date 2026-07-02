import type { HighlightLine } from '../components/HighlightedTextarea';

const KEYWORD_PATTERN = /^(and|or|xor|implies|not|as|is|in|contains|div|mod|true|false)$/;

const TOKEN_PATTERN = /('(?:\\.|[^'\\])*')|(%[A-Za-z_][\w]*)|(\d+(?:\.\d+)?)|([A-Za-z_][\w]*)|(!=|<=|>=)|(\s+)|(.)/g;

/**
 * Lightweight FHIRPath syntax highlighter for the expression editor: string
 * literals, `%variables`, numbers, boolean/logical keywords, and function
 * calls (an identifier immediately followed by `(`). Everything else
 * (property-path segments, punctuation) is left in the default text color —
 * deliberately restrained rather than coloring every token.
 */
export function highlightFhirPathExpression(text: string): HighlightLine[] {
  return text.split('\n').map((line) => {
    const segments: { text: string; color: string }[] = [];
    const pattern = new RegExp(TOKEN_PATTERN);
    let match: RegExpExecArray | null;

    while ((match = pattern.exec(line))) {
      const value = match[0];
      let color = 'var(--text2)';

      if (match[1]) {
        color = 'var(--chip-teal-fg)';
      } else if (match[2]) {
        color = 'var(--fail)';
      } else if (match[3]) {
        color = 'var(--chip-amb-fg)';
      } else if (match[4]) {
        if (KEYWORD_PATTERN.test(value)) {
          color = 'var(--accent)';
        } else if (line[pattern.lastIndex] === '(') {
          color = 'var(--chip-pink-fg)';
        } else {
          color = 'var(--text)';
        }
      }

      segments.push({ text: value, color });
    }

    if (segments.length === 0) {
      segments.push({ text: ' ', color: 'var(--text2)' });
    }
    return { segments };
  });
}
