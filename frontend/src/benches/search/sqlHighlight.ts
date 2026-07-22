/** T-SQL reserved words that appear in `Ignixa.Search.Sql`'s emitted SQL (see `SqlBuilder`/`EmitPredicate`) —
 * not every T-SQL keyword, only the ones this compiler's output actually uses. */
const KEYWORD_PATTERN =
  /^(WITH|AS|SELECT|DISTINCT|TOP|FROM|INNER|LEFT|RIGHT|OUTER|JOIN|ON|AND|OR|NOT|WHERE|ORDER|BY|ASC|DESC|UNION|ALL|EXISTS|CASE|WHEN|THEN|ELSE|END|NULL|IS|IN|LIKE|OVER|COLLATE|CAST|BIT)$/i;

const TOKEN_PATTERN = /('(?:[^']|'')*')|(@[A-Za-z_]\w*)|(\d+(?:\.\d+)?)|([A-Za-z_][\w]*)|(\s+)|(.)/g;

export interface SqlToken {
  text: string;
  color: string;
}

/**
 * Lightweight syntax highlighter for the emitted-SQL panel: string literals, `@parameter` placeholders,
 * numbers, and reserved words each get a distinct color; a function call (an identifier immediately
 * followed by `(`, e.g. `COUNT_BIG(`) gets its own color too, distinct from a plain table/column
 * identifier. Punctuation and whitespace are left at the base text color — deliberately restrained rather
 * than coloring every character. Operates on a flat string (not per-line, unlike `HighlightedTextarea`'s
 * editors) since this only ever colors read-only text inside a `<pre>`, never an editable textarea.
 */
export function tokenizeSql(text: string): SqlToken[] {
  const tokens: SqlToken[] = [];
  const pattern = new RegExp(TOKEN_PATTERN);
  let match: RegExpExecArray | null;

  while ((match = pattern.exec(text))) {
    const value = match[0];
    let color = 'var(--text)';

    if (match[1]) {
      color = 'var(--chip-teal-fg)';
    } else if (match[2]) {
      color = 'var(--chip-pink-fg)';
    } else if (match[3]) {
      color = 'var(--chip-amb-fg)';
    } else if (match[4]) {
      if (KEYWORD_PATTERN.test(value)) {
        color = 'var(--accent)';
      } else if (text[pattern.lastIndex] === '(') {
        color = 'var(--chip-ind-fg)';
      } else {
        color = 'var(--text)';
      }
    } else if (match[6]) {
      color = 'var(--text2)';
    }

    tokens.push({ text: value, color });
  }

  return tokens;
}
