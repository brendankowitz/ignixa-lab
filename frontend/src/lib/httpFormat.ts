/**
 * Pure helpers for rendering captured HTTP request/response bodies as
 * structured, colorized output in the Runner's Request/Response tabs
 * (see {@link ../components/HttpMessage}). No DOM or React dependency —
 * these only produce data for the presentational layer to render.
 */

/** Result of {@link prettyJson}: the text to display, and whether it is JSON (and so eligible for token coloring). */
export interface PrettyJsonResult {
  text: string;
  isJson: boolean;
}

/**
 * Pretty-prints a captured HTTP body if it parses as JSON; otherwise returns
 * it unchanged. Never throws — bodies are best-effort captures and may be
 * empty, non-JSON, or malformed.
 */
export function prettyJson(body: string | null): PrettyJsonResult {
  if (body === null) {
    return { text: '', isJson: false };
  }
  const trimmed = body.trim();
  if (trimmed.length === 0) {
    return { text: '', isJson: false };
  }
  try {
    const parsed = JSON.parse(trimmed);
    return { text: JSON.stringify(parsed, null, 2), isJson: true };
  } catch {
    return { text: body, isJson: false };
  }
}

/** A syntax-highlighting class for one lexical piece of pretty-printed JSON. */
export type JsonTokenClass = 'key' | 'string' | 'number' | 'boolean' | 'null' | 'punct' | 'plain';

/** One tokenized piece of pretty-printed JSON, ready to render as a classed `<span>`. */
export interface JsonToken {
  value: string;
  cls: JsonTokenClass;
}

/**
 * Matches, in order of precedence, a JSON string (handling escaped quotes), a
 * number, the `true`/`false`/`null` literals, a structural punctuation
 * character, or a run of whitespace. Scanned as a single global regex pass;
 * any input not covered by an alternative (there shouldn't be any, since the
 * input is always `JSON.stringify` output) falls through as `plain`.
 */
const JSON_TOKEN_PATTERN =
  /"(?:\\.|[^"\\])*"|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null|[{}[\],:]|[ \t\r\n]+/g;

/**
 * Splits pretty-printed JSON text into classed tokens for manual (non-HTML)
 * syntax coloring. A string token is classed `key` when it is immediately
 * followed (ignoring whitespace) by a colon, `string` otherwise.
 */
export function tokenizeJson(text: string): JsonToken[] {
  const tokens: JsonToken[] = [];
  let lastIndex = 0;
  let match: RegExpExecArray | null;

  JSON_TOKEN_PATTERN.lastIndex = 0;
  while ((match = JSON_TOKEN_PATTERN.exec(text)) !== null) {
    if (match.index > lastIndex) {
      tokens.push({ value: text.slice(lastIndex, match.index), cls: 'plain' });
    }
    tokens.push({ value: match[0], cls: classifyToken(match[0], text, JSON_TOKEN_PATTERN.lastIndex) });
    lastIndex = JSON_TOKEN_PATTERN.lastIndex;
  }
  if (lastIndex < text.length) {
    tokens.push({ value: text.slice(lastIndex), cls: 'plain' });
  }
  return tokens;
}

function classifyToken(value: string, text: string, endIndex: number): JsonTokenClass {
  const first = value[0];
  if (first === '"') {
    return isFollowedByColon(text, endIndex) ? 'key' : 'string';
  }
  if (first === '-' || (first >= '0' && first <= '9')) {
    return 'number';
  }
  if (value === 'true' || value === 'false') {
    return 'boolean';
  }
  if (value === 'null') {
    return 'null';
  }
  if (value.length === 1 && '{}[],:'.includes(first)) {
    return 'punct';
  }
  return 'plain';
}

/** Looks ahead from `index`, skipping whitespace, to see whether the next character is a colon. */
function isFollowedByColon(text: string, index: number): boolean {
  let i = index;
  while (i < text.length && /\s/.test(text[i])) {
    i += 1;
  }
  return text[i] === ':';
}
