import type { SourceOrigin, SyntaxNode } from './searchTypes';

/** One slice of a parameter's key or value string. `node` is the deepest syntax node covering it (for
 * click-to-trace), or null for the plain text between/around spans. */
export interface Segment {
  start: number;
  length: number;
  node: SyntaxNode | null;
}

/** Flattens a syntax tree into non-overlapping, ordered segments over `text` (one parameter's key or value
 * string), keeping only spans whose `origin` matches `origin`. Offsets are relative to `text`. Gaps between
 * spans (e.g. the '$' in a composite, or a comma between alternatives) become plain segments with node=null,
 * so the renderer can lay the whole string out left-to-right without losing characters. */
export function spanSegments(text: string, root: SyntaxNode | null, origin: SourceOrigin): Segment[] {
  const cuts = new Set<number>([0, text.length]);
  const covering: { start: number; end: number; node: SyntaxNode }[] = [];

  const walk = (node: SyntaxNode) => {
    if (node.span.origin === origin) {
      const start = Math.max(0, node.span.start);
      const end = Math.min(text.length, node.span.start + node.span.length);
      if (end > start) {
        cuts.add(start);
        cuts.add(end);
        covering.push({ start, end, node });
      }
    }
    for (const child of node.children) {
      walk(child);
    }
  };
  if (root) {
    walk(root);
  }

  const boundaries = [...cuts].sort((a, b) => a - b);
  const segments: Segment[] = [];
  for (let i = 0; i < boundaries.length - 1; i += 1) {
    const start = boundaries[i];
    const end = boundaries[i + 1];
    if (end <= start) {
      continue;
    }
    // Deepest covering node wins (a child span sits inside its parent); ties broken by smallest range.
    let best: SyntaxNode | null = null;
    let bestWidth = Infinity;
    for (const candidate of covering) {
      if (candidate.start <= start && candidate.end >= end) {
        const width = candidate.end - candidate.start;
        if (width < bestWidth) {
          best = candidate.node;
          bestWidth = width;
        }
      }
    }
    segments.push({ start, length: end - start, node: best });
  }
  return segments;
}
