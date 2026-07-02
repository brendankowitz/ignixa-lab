/**
 * A small client-side FHIRPath-subset engine, ported from the Expression
 * Benches mockup. Used only to power the FML and SQL-on-FHIR *mock* benches'
 * own path evaluation (`forEach`, `column.path`, FML rule right-hand sides) —
 * the real FHIRPath bench talks to the actual backend engine and does not
 * use this module. Not a full FHIRPath implementation.
 */

type MiniToken =
  | { t: 'id'; v: string }
  | { t: 'num'; v: number }
  | { t: 'str'; v: string }
  | { t: 'var'; v: string }
  | { t: 'op'; v: string };

export type MiniNode =
  | { k: 'lit'; v: string | number | boolean }
  | { k: 'var'; v: string }
  | { k: 'paren'; e: MiniNode }
  | { k: 'path'; name: string }
  | { k: 'fn'; name: string; args: MiniNode[] }
  | { k: 'chain'; parts: MiniNode[] }
  | { k: 'index'; e: MiniNode }
  | { k: 'bin'; op: string; l: MiniNode; r: MiniNode };

export interface MiniEnv {
  vars: Record<string, unknown>;
}

function tokenize(source: string): MiniToken[] {
  const tokens: MiniToken[] = [];
  let i = 0;
  const isIdStart = (c: string) => /[A-Za-z_$]/.test(c);
  const isIdChar = (c: string) => /[A-Za-z0-9_$]/.test(c);

  while (i < source.length) {
    const c = source[i];
    if (/\s/.test(c)) {
      i++;
      continue;
    }
    if (isIdStart(c)) {
      let j = i + 1;
      while (j < source.length && isIdChar(source[j])) j++;
      tokens.push({ t: 'id', v: source.slice(i, j) });
      i = j;
      continue;
    }
    if (/[0-9]/.test(c)) {
      let j = i;
      while (j < source.length && (/[0-9]/.test(source[j]) || (source[j] === '.' && /[0-9]/.test(source[j + 1] ?? '')))) j++;
      tokens.push({ t: 'num', v: Number.parseFloat(source.slice(i, j)) });
      i = j;
      continue;
    }
    if (c === "'") {
      let j = i + 1;
      let value = '';
      while (j < source.length && source[j] !== "'") {
        value += source[j];
        j++;
      }
      if (j >= source.length) throw new Error('Unterminated string literal');
      tokens.push({ t: 'str', v: value });
      i = j + 1;
      continue;
    }
    if (c === '%') {
      let j = i + 1;
      while (j < source.length && isIdChar(source[j])) j++;
      tokens.push({ t: 'var', v: source.slice(i + 1, j) });
      i = j;
      continue;
    }
    const two = source.slice(i, i + 2);
    if (two === '!=' || two === '<=' || two === '>=') {
      tokens.push({ t: 'op', v: two });
      i += 2;
      continue;
    }
    if ('.()[],=<>|+-*/&'.includes(c)) {
      tokens.push({ t: 'op', v: c });
      i++;
      continue;
    }
    throw new Error(`Unexpected character '${c}' at position ${i}`);
  }
  return tokens;
}

/** Parses a FHIRPath-subset expression into a {@link MiniNode} tree. */
export function parseMiniFhirPath(source: string): MiniNode {
  const tokens = tokenize(source);
  let position = 0;
  const peek = () => tokens[position];
  const eat = () => tokens[position++];
  const expect = (value: string) => {
    const token = eat();
    if (!token || token.v !== value) throw new Error(`Expected '${value}'`);
  };

  const parseArgs = (): MiniNode[] => {
    const args: MiniNode[] = [];
    if (peek() && peek().v !== ')') {
      args.push(parseExpr());
      while (peek() && peek().v === ',') {
        eat();
        args.push(parseExpr());
      }
    }
    expect(')');
    return args;
  };

  const parsePrimary = (): MiniNode => {
    const token = peek();
    if (!token) throw new Error('Unexpected end of expression');
    if (token.t === 'num' || token.t === 'str') {
      eat();
      return { k: 'lit', v: token.v };
    }
    if (token.t === 'var') {
      eat();
      return { k: 'var', v: token.v };
    }
    if (token.t === 'op' && token.v === '(') {
      eat();
      const inner = parseExpr();
      expect(')');
      return { k: 'paren', e: inner };
    }
    if (token.t === 'id') {
      eat();
      if (token.v === 'true' || token.v === 'false') return { k: 'lit', v: token.v === 'true' };
      if (peek() && peek().v === '(') {
        eat();
        return { k: 'fn', name: token.v, args: parseArgs() };
      }
      return { k: 'path', name: token.v };
    }
    throw new Error(`Unexpected token '${token.v}'`);
  };

  const parsePostfix = (): MiniNode => {
    const first = parsePrimary();
    const parts = [first];
    while (peek()) {
      if (peek().v === '.') {
        eat();
        const token = eat();
        if (!token || token.t !== 'id') throw new Error("Expected a name after '.'");
        if (peek() && peek().v === '(') {
          eat();
          parts.push({ k: 'fn', name: token.v, args: parseArgs() });
        } else {
          parts.push({ k: 'path', name: token.v });
        }
      } else if (peek().v === '[') {
        eat();
        const inner = parseExpr();
        expect(']');
        parts.push({ k: 'index', e: inner });
      } else {
        break;
      }
    }
    return parts.length === 1 ? first : { k: 'chain', parts };
  };

  const parseMul = (): MiniNode => {
    let left = parsePostfix();
    while (peek() && (peek().v === '*' || peek().v === '/')) {
      const op = (eat() as MiniToken).v as string;
      left = { k: 'bin', op, l: left, r: parsePostfix() };
    }
    return left;
  };

  const parseAdd = (): MiniNode => {
    let left = parseMul();
    while (peek() && ['+', '-', '&'].includes(peek().v as string)) {
      const op = (eat() as MiniToken).v as string;
      left = { k: 'bin', op, l: left, r: parseMul() };
    }
    return left;
  };

  const parseUnion = (): MiniNode => {
    let left = parseAdd();
    while (peek() && peek().v === '|') {
      eat();
      left = { k: 'bin', op: '|', l: left, r: parseAdd() };
    }
    return left;
  };

  const parseCmp = (): MiniNode => {
    let left = parseUnion();
    while (peek() && ['<', '>', '<=', '>='].includes(peek().v as string)) {
      const op = (eat() as MiniToken).v as string;
      left = { k: 'bin', op, l: left, r: parseUnion() };
    }
    return left;
  };

  const parseEq = (): MiniNode => {
    let left = parseCmp();
    while (peek() && (peek().v === '=' || peek().v === '!=')) {
      const op = (eat() as MiniToken).v as string;
      left = { k: 'bin', op, l: left, r: parseCmp() };
    }
    return left;
  };

  const parseAnd = (): MiniNode => {
    let left = parseEq();
    while (peek() && peek().t === 'id' && peek().v === 'and') {
      eat();
      left = { k: 'bin', op: 'and', l: left, r: parseEq() };
    }
    return left;
  };

  function parseExpr(): MiniNode {
    let left = parseAnd();
    while (peek() && peek().t === 'id' && peek().v === 'or') {
      eat();
      left = { k: 'bin', op: 'or', l: left, r: parseAnd() };
    }
    return left;
  }

  const result = parseExpr();
  if (position < tokens.length) throw new Error(`Unexpected token '${tokens[position].v}'`);
  return result;
}

function getField(item: unknown, name: string, out: unknown[]): void {
  if (item === null || typeof item !== 'object') return;
  const record = item as Record<string, unknown>;
  let value = record[name];
  if (value === undefined) {
    for (const key of Object.keys(record)) {
      if (key.length > name.length && key.startsWith(name) && /[A-Z]/.test(key[name.length])) {
        value = record[key];
        break;
      }
    }
  }
  if (value === undefined) return;
  if (Array.isArray(value)) out.push(...value);
  else out.push(value);
}

function isTruthy(collection: unknown[]): boolean {
  return collection.length > 0 && collection[0] !== false;
}

function evalFunction(node: Extract<MiniNode, { k: 'fn' }>, collection: unknown[], env: MiniEnv): unknown[] {
  const args = node.args;
  const evalArg = (index: number) => evaluateMiniFhirPath(args[index], collection, env);
  switch (node.name) {
    case 'where':
      return collection.filter((item) => isTruthy(evaluateMiniFhirPath(args[0], [item], env)));
    case 'select': {
      const out: unknown[] = [];
      for (const item of collection) out.push(...evaluateMiniFhirPath(args[0], [item], env));
      return out;
    }
    case 'exists':
      return [args.length ? collection.some((item) => isTruthy(evaluateMiniFhirPath(args[0], [item], env))) : collection.length > 0];
    case 'empty':
      return [collection.length === 0];
    case 'count':
      return [collection.length];
    case 'first':
      return collection.slice(0, 1);
    case 'last':
      return collection.slice(-1);
    case 'tail':
      return collection.slice(1);
    case 'join': {
      const separator = args.length ? String(evalArg(0)[0]) : '';
      return [collection.map((x) => (typeof x === 'object' && x !== null ? JSON.stringify(x) : String(x))).join(separator)];
    }
    case 'distinct': {
      const seen = new Set<string>();
      const out: unknown[] = [];
      for (const item of collection) {
        const key = JSON.stringify(item);
        if (!seen.has(key)) {
          seen.add(key);
          out.push(item);
        }
      }
      return out;
    }
    case 'not':
      return collection.length === 0 ? [] : [!isTruthy(collection)];
    case 'toString':
      return collection.length ? [typeof collection[0] === 'object' ? JSON.stringify(collection[0]) : String(collection[0])] : [];
    default:
      throw new Error(`Unsupported function: ${node.name}()`);
  }
}

/** Evaluates a {@link MiniNode} against a collection, FHIRPath-style (every result is itself a collection). */
export function evaluateMiniFhirPath(node: MiniNode, collection: unknown[], env: MiniEnv): unknown[] {
  switch (node.k) {
    case 'lit':
      return [node.v];
    case 'var': {
      const value = env.vars[node.v];
      if (value === undefined) throw new Error(`Unknown variable %${node.v}`);
      return Array.isArray(value) ? value : [value];
    }
    case 'paren':
      return evaluateMiniFhirPath(node.e, collection, env);
    case 'path': {
      if (node.name === '$this') return collection;
      const out: unknown[] = [];
      for (const item of collection) {
        const record = item as Record<string, unknown> | null;
        if (record && typeof record === 'object' && record.resourceType === node.name) {
          out.push(item);
          continue;
        }
        getField(item, node.name, out);
      }
      return out;
    }
    case 'fn':
      return evalFunction(node, collection, env);
    case 'chain': {
      let current = collection;
      for (const part of node.parts) {
        if (part.k === 'index') {
          const index = evaluateMiniFhirPath(part.e, current, env)[0] as number;
          current = current[index] !== undefined ? [current[index]] : [];
        } else {
          current = evaluateMiniFhirPath(part, current, env);
        }
      }
      return current;
    }
    case 'bin': {
      const op = node.op;
      const left = evaluateMiniFhirPath(node.l, collection, env);
      const right = evaluateMiniFhirPath(node.r, collection, env);
      if (op === 'and') return [isTruthy(left) && isTruthy(right)];
      if (op === 'or') return [isTruthy(left) || isTruthy(right)];
      if (op === '|') {
        const out = [...left];
        for (const r of right) if (!out.some((x) => JSON.stringify(x) === JSON.stringify(r))) out.push(r);
        return out;
      }
      if (op === '&') {
        const l = left.length ? String(left[0]) : '';
        const r = right.length ? String(right[0]) : '';
        return [l + r];
      }
      if (!left.length || !right.length) return [];
      const l = left[0] as string | number;
      const r = right[0] as string | number;
      switch (op) {
        case '=':
          return [JSON.stringify(l) === JSON.stringify(r)];
        case '!=':
          return [JSON.stringify(l) !== JSON.stringify(r)];
        case '<':
          return [l < r];
        case '>':
          return [l > r];
        case '<=':
          return [l <= r];
        case '>=':
          return [l >= r];
        case '+':
          return [(l as number) + (r as number)];
        case '-':
          return [(l as number) - (r as number)];
        case '*':
          return [(l as number) * (r as number)];
        case '/':
          return [(l as number) / (r as number)];
        default:
          return [];
      }
    }
    default:
      return [];
  }
}
