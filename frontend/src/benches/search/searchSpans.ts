import type { SourceOrigin, SyntaxNode } from './searchTypes';

/** One slice of a parameter's key or value string. `node` is the deepest syntax node covering it (for
 * click-to-trace), or null for the plain text between/around spans. */
export interface Segment {
  start: number;
  length: number;
  node: SyntaxNode | null;
}

/** One candidate interval `cutByCovers` may assign a slice to — `[start, end)`, half-open like every other
 * offset pair in this module. */
export interface Cover<T> {
  start: number;
  end: number;
  payload: T;
}

/** One non-overlapping, ordered slice of `[0, length)`. `payload` is the smallest cover fully containing the
 * slice (a nested/child interval wins over a wider parent one), or null where nothing covers it. */
export interface CutSegment<T> {
  start: number;
  length: number;
  payload: T | null;
}

/**
 * Splits `[0, length)` into non-overlapping, ordered segments at every cover's boundary, assigning each
 * segment the smallest cover that fully contains it (so a nested interval — a child syntax node, an
 * `_include` stage's SQL range within the whole statement — wins over a wider one enclosing it). A gap no
 * cover touches becomes a segment with `payload: null`, so a caller can lay the full `[0, length)` range out
 * left-to-right without losing any of it. Shared by `spanSegments` (over one parameter's key/value string,
 * covers from a syntax tree) and `SearchBench.tsx`'s SQL-range segmenting (over the whole emitted SQL text,
 * covers from a flat `SqlTextRange[]`) — the cut-and-assign algorithm is identical, only how each caller
 * gathers its covers differs.
 */
export function cutByCovers<T>(length: number, covers: Cover<T>[]): CutSegment<T>[] {
  const cuts = new Set<number>([0, length]);
  for (const cover of covers) {
    cuts.add(cover.start);
    cuts.add(cover.end);
  }

  const boundaries = [...cuts].sort((a, b) => a - b);
  const segments: CutSegment<T>[] = [];
  for (let i = 0; i < boundaries.length - 1; i += 1) {
    const start = boundaries[i];
    const end = boundaries[i + 1];
    if (end <= start) {
      continue;
    }
    let best: T | null = null;
    let bestWidth = Infinity;
    for (const cover of covers) {
      if (cover.start <= start && cover.end >= end) {
        const width = cover.end - cover.start;
        if (width < bestWidth) {
          best = cover.payload;
          bestWidth = width;
        }
      }
    }
    segments.push({ start, length: end - start, payload: best });
  }
  return segments;
}

/** Flattens a syntax tree into non-overlapping, ordered segments over `text` (one parameter's key or value
 * string), keeping only spans whose `origin` matches `origin`. Offsets are relative to `text`. Gaps between
 * spans (e.g. the '$' in a composite, or a comma between alternatives) become plain segments with node=null,
 * so the renderer can lay the whole string out left-to-right without losing characters. */
export function spanSegments(text: string, root: SyntaxNode | null, origin: SourceOrigin): Segment[] {
  const covers: Cover<SyntaxNode>[] = [];

  const walk = (node: SyntaxNode) => {
    if (node.span.origin === origin) {
      const start = Math.max(0, node.span.start);
      const end = Math.min(text.length, node.span.start + node.span.length);
      if (end > start) {
        covers.push({ start, end, payload: node });
      }
    }
    for (const child of node.children) {
      walk(child);
    }
  };
  if (root) {
    walk(root);
  }

  return cutByCovers(text.length, covers).map((s) => ({ start: s.start, length: s.length, node: s.payload }));
}
