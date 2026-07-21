/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { spanSegments } from './searchSpans.ts';
import type { SyntaxNode } from './searchTypes.ts';

// birthdate=gt2000-01-01 → value = "gt2000-01-01"; a value-origin syntax node covering the prefix+value.
const valueSyntax: SyntaxNode = {
  kind: 'atomic',
  span: { origin: 'Value', start: 0, length: 12 },
  children: [],
};

test('a single span over the whole value yields one highlighted segment covering it', () => {
  const segments = spanSegments('gt2000-01-01', valueSyntax, 'Value');
  const highlighted = segments.filter((s) => s.node !== null);
  assert.equal(highlighted.length, 1);
  assert.equal('gt2000-01-01'.slice(highlighted[0].start, highlighted[0].start + highlighted[0].length), 'gt2000-01-01');
});

test('a nested child span produces a sub-segment inside its parent', () => {
  const nested: SyntaxNode = {
    kind: 'composite',
    span: { origin: 'Value', start: 0, length: 11 }, // "8480-6$high"
    children: [
      { kind: 'component', span: { origin: 'Value', start: 0, length: 6 }, children: [] }, // "8480-6"
      { kind: 'component', span: { origin: 'Value', start: 7, length: 4 }, children: [] }, // "high"
    ],
  };
  const segments = spanSegments('8480-6$high', nested, 'Value');
  const texts = segments.map((s) => '8480-6$high'.slice(s.start, s.start + s.length));
  // The '$' separator between the two component spans must appear as a plain (node === null) segment.
  assert.ok(texts.includes('$'));
});

test('spans whose origin does not match the requested string are skipped', () => {
  const keySpan: SyntaxNode = { kind: 'k', span: { origin: 'Key', start: 0, length: 3 }, children: [] };
  const segments = spanSegments('abc', keySpan, 'Value'); // asking for Value segments, node is Key-origin
  assert.equal(segments.filter((s) => s.node !== null).length, 0);
});
