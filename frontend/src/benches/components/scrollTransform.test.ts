/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { scrollTransform } from './scrollTransform.ts';

test('keeps the highlight layer stationary at zero scroll offsets', () => {
  assert.equal(scrollTransform(0, 0), 'translate(0px, 0px)');
});

test('moves the highlight layer opposite positive scroll offsets', () => {
  assert.equal(scrollTransform(120, 48), 'translate(-120px, -48px)');
});

test('preserves fractional scroll offsets without rounding', () => {
  assert.equal(scrollTransform(532.6667, 14.25), 'translate(-532.6667px, -14.25px)');
});
