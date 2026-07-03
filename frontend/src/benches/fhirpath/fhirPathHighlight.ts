import type { HighlightLine } from '../components/HighlightedTextarea';

const KEYWORD_PATTERN = /^(and|or|xor|implies|not|as|is|in|contains|div|mod|true|false)$/;

const TOKEN_PATTERN = /('(?:\\.|[^'\\])*')|(%[A-Za-z_][\w]*)|(\d+(?:\.\d+)?)|([A-Za-z_][\w]*)|(!=|<=|>=)|(\s+)|(.)/g;

/**
 * Lightweight FHIRPath syntax highlighter for the expression editor: string
 * literals, `%variables`, numbers, and boolean/logical keywords each get a
 * distinct color; function calls (an identifier immediately followed by `(`)
 * get their own color too. Plain identifiers (property-path segments) fall
 * back to the base text color (`--text`); punctuation, whitespace, and
 * comparison operators (`!= <= >=`) are left at the token stream's default
 * (`--text2`) — deliberately restrained rather than coloring every token.
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
