import { KEYWORD_PATTERN } from './fhirPathHighlight.ts';

export interface JsonPathSelection {
  path: string;
  start: number;
  end: number;
}

export type JsonPathResolution =
  | { kind: 'match'; selection: JsonPathSelection }
  | { kind: 'none' }
  | { kind: 'invalid' };

type PathSegment = string | number;

interface TokenSelection {
  path: string;
  start: number;
  end: number;
  depth: number;
}

export function resolveJsonPathAtOffset(source: string, offset: number): JsonPathResolution {
  if (offset < 0 || offset >= source.length) {
    return { kind: 'none' };
  }

  try {
    JSON.parse(source);
  } catch {
    return { kind: 'invalid' };
  }

  const parser = new JsonSpanParser(source);

  try {
    const selections = parser.parse();
    const match = findDeepestSelection(selections, offset);
    if (!match) {
      return { kind: 'none' };
    }
    return {
      kind: 'match',
      selection: {
        path: match.path,
        start: match.start,
        end: match.end,
      },
    };
  } catch (error) {
    // JSON.parse already accepted this source above, so a throw here means the
    // hand-rolled span parser disagrees with the platform grammar (a bug or a
    // stack-depth limit) rather than genuinely malformed input — log it so that
    // divergence isn't silently indistinguishable from the user's own typo.
    console.error('jsonPathResolver: span parser failed on JSON.parse-valid source', error);
    return { kind: 'invalid' };
  }
}

function findDeepestSelection(selections: TokenSelection[], offset: number): TokenSelection | undefined {
  let best: TokenSelection | undefined;

  for (const selection of selections) {
    if (offset < selection.start || offset >= selection.end) {
      continue;
    }
    if (
      best === undefined ||
      selection.depth > best.depth ||
      (selection.depth === best.depth && selection.end - selection.start > best.end - best.start)
    ) {
      best = selection;
    }
  }

  return best;
}

class JsonSpanParser {
  private readonly selections: TokenSelection[] = [];
  private index = 0;
  private readonly source: string;

  constructor(source: string) {
    this.source = source;
  }

  parse(): TokenSelection[] {
    this.parseValue([]);
    this.skipWhitespace();

    if (this.index !== this.source.length) {
      throw new Error('Unexpected trailing content');
    }

    return this.selections;
  }

  private parseValue(path: PathSegment[]): void {
    this.skipWhitespace();

    const ch = this.source[this.index];
    if (ch === '{') {
      this.parseObject(path);
      return;
    }
    if (ch === '[') {
      this.parseArray(path);
      return;
    }
    if (ch === '"') {
      const token = this.parseStringToken();
      this.addSelection(path, token.start, token.end);
      return;
    }
    if (ch === 't' || ch === 'f' || ch === 'n') {
      const token = this.parseLiteral();
      this.addSelection(path, token.start, token.end);
      return;
    }
    if (ch === '-' || this.isDigit(ch)) {
      const token = this.parseNumberToken();
      this.addSelection(path, token.start, token.end);
      return;
    }

    throw new Error(`Unexpected token at ${this.index}`);
  }

  private parseObject(path: PathSegment[]): void {
    this.expect('{');
    this.skipWhitespace();

    if (this.peek('}')) {
      this.index++;
      return;
    }

    while (true) {
      this.skipWhitespace();
      const key = this.parseStringToken();
      const keyPath = [...path, key.value];
      this.addSelection(keyPath, key.start, key.end);

      this.skipWhitespace();
      this.expect(':');
      this.parseValue(keyPath);

      this.skipWhitespace();
      if (this.peek(',')) {
        this.index++;
        continue;
      }
      if (this.peek('}')) {
        this.index++;
        return;
      }
      throw new Error(`Unexpected object separator at ${this.index}`);
    }
  }

  private parseArray(path: PathSegment[]): void {
    this.expect('[');
    this.skipWhitespace();

    if (this.peek(']')) {
      this.index++;
      return;
    }

    let itemIndex = 0;
    while (true) {
      this.parseValue([...path, itemIndex]);

      this.skipWhitespace();
      if (this.peek(',')) {
        this.index++;
        itemIndex++;
        continue;
      }
      if (this.peek(']')) {
        this.index++;
        return;
      }
      throw new Error(`Unexpected array separator at ${this.index}`);
    }
  }

  private parseStringToken(): { value: string; start: number; end: number } {
    const start = this.index;
    this.expect('"');

    while (this.index < this.source.length) {
      const ch = this.source[this.index];
      if (ch === '"') {
        const end = this.index + 1;
        const value = JSON.parse(this.source.slice(start, end)) as string;
        this.index = end;
        return { value, start, end };
      }
      if (ch === '\\') {
        this.index += 2;
        if (this.index > this.source.length) {
          break;
        }
        continue;
      }
      this.index++;
    }

    throw new Error('Unterminated string literal');
  }

  private parseNumberToken(): { start: number; end: number } {
    const start = this.index;

    if (this.peek('-')) {
      this.index++;
    }

    if (!this.isDigit(this.source[this.index])) {
      throw new Error(`Invalid number at ${this.index}`);
    }

    if (this.peek('0')) {
      this.index++;
    } else {
      while (this.isDigit(this.source[this.index])) {
        this.index++;
      }
    }

    if (this.peek('.')) {
      this.index++;
      if (!this.isDigit(this.source[this.index])) {
        throw new Error(`Invalid fraction at ${this.index}`);
      }
      while (this.isDigit(this.source[this.index])) {
        this.index++;
      }
    }

    if (this.peek('e') || this.peek('E')) {
      this.index++;
      if (this.peek('+') || this.peek('-')) {
        this.index++;
      }
      if (!this.isDigit(this.source[this.index])) {
        throw new Error(`Invalid exponent at ${this.index}`);
      }
      while (this.isDigit(this.source[this.index])) {
        this.index++;
      }
    }

    return { start, end: this.index };
  }

  private parseLiteral(): { start: number; end: number } {
    const start = this.index;

    if (this.source.startsWith('true', this.index)) {
      this.index += 4;
      return { start, end: this.index };
    }
    if (this.source.startsWith('false', this.index)) {
      this.index += 5;
      return { start, end: this.index };
    }
    if (this.source.startsWith('null', this.index)) {
      this.index += 4;
      return { start, end: this.index };
    }

    throw new Error(`Invalid literal at ${this.index}`);
  }

  private addSelection(path: PathSegment[], start: number, end: number): void {
    if (path.length === 0) {
      // A root-level scalar (e.g. source is just `"x"`) has no element to name —
      // formatPath([]) would yield an empty string, which the status bar can't
      // distinguish from "nothing selected".
      return;
    }
    this.selections.push({
      path: formatPath(path),
      start,
      end,
      depth: path.length,
    });
  }

  private skipWhitespace(): void {
    while (this.index < this.source.length && /\s/u.test(this.source[this.index])) {
      this.index++;
    }
  }

  private expect(char: string): void {
    if (this.source[this.index] !== char) {
      throw new Error(`Expected ${char} at ${this.index}`);
    }
    this.index++;
  }

  private peek(char: string): boolean {
    return this.source[this.index] === char;
  }

  private isDigit(ch: string | undefined): boolean {
    return ch !== undefined && ch >= '0' && ch <= '9';
  }
}

function formatPath(path: PathSegment[]): string {
  let result = '';

  for (const segment of path) {
    if (typeof segment === 'number') {
      result += `[${segment}]`;
      continue;
    }

    const formatted = formatPropertyName(segment);
    result += result.length === 0 ? formatted : `.${formatted}`;
  }

  return result;
}

function formatPropertyName(name: string): string {
  if (/^[A-Za-z_][A-Za-z0-9_]*$/u.test(name) && !KEYWORD_PATTERN.test(name)) {
    return name;
  }

  return `\`${name.replace(/\\/gu, '\\\\').replace(/`/gu, '\\`')}\``;
}
