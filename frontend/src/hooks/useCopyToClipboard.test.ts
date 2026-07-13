/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { copyStatusReducer } from './useCopyToClipboard.ts';

test('idle + copied => copied', () => {
  assert.equal(copyStatusReducer('idle', { type: 'copied' }), 'copied');
});

test('idle + failed => failed', () => {
  assert.equal(copyStatusReducer('idle', { type: 'failed' }), 'failed');
});

test('failed + reset => idle', () => {
  assert.equal(copyStatusReducer('failed', { type: 'reset' }), 'idle');
});

test('copied + reset => idle', () => {
  assert.equal(copyStatusReducer('copied', { type: 'reset' }), 'idle');
});
