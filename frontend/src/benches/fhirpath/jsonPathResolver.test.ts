/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { resolveJsonPathAtOffset } from './jsonPathResolver.ts';

const source = JSON.stringify(
  {
    name: [
      {
        family: 'Chalmers',
        given: ['Peter', 'James'],
      },
    ],
    'value-set': {
      'a`b': true,
    },
  },
  null,
  2,
);

test('nested property key family resolves to name[0].family', () => {
  const keyOffset = source.indexOf('"family"') + 2;
  const valueOffset = source.indexOf('"Chalmers"') + 2;

  const keyResult = resolveJsonPathAtOffset(source, keyOffset);
  const valueResult = resolveJsonPathAtOffset(source, valueOffset);

  assert.equal(keyResult.kind, 'match');
  assert.equal(valueResult.kind, 'match');
  if (keyResult.kind !== 'match' || valueResult.kind !== 'match') {
    return;
  }
  assert.equal(keyResult.selection.path, 'name[0].family');
  assert.equal(valueResult.selection.path, 'name[0].family');
  assert.equal(source.slice(keyResult.selection.start, keyResult.selection.end), '"family"');
  assert.equal(source.slice(valueResult.selection.start, valueResult.selection.end), '"Chalmers"');
});

test('second primitive array value James resolves to name[0].given[1]', () => {
  const offset = source.indexOf('"James"') + 2;
  const result = resolveJsonPathAtOffset(source, offset);

  assert.equal(result.kind, 'match');
  if (result.kind !== 'match') {
    return;
  }
  assert.equal(result.selection.path, 'name[0].given[1]');
  assert.equal(source.slice(result.selection.start, result.selection.end), '"James"');
});

test('object-valued name key resolves to name', () => {
  const result = resolveJsonPathAtOffset(source, source.indexOf('"name"') + 2);

  assert.equal(result.kind, 'match');
  if (result.kind !== 'match') {
    return;
  }
  assert.equal(result.selection.path, 'name');
  assert.equal(source.slice(result.selection.start, result.selection.end), '"name"');
});

test('non-bare names and backticks resolve with escaping', () => {
  const result = resolveJsonPathAtOffset(source, source.indexOf('"a`b"') + 2);

  assert.equal(result.kind, 'match');
  if (result.kind !== 'match') {
    return;
  }
  assert.equal(result.selection.path, '`value-set`.`a\\`b`');
  assert.equal(source.slice(result.selection.start, result.selection.end), '"a`b"');
});

test('whitespace, punctuation, and exact token end return none', () => {
  const whitespaceOffset = source.indexOf('"Chalmers"') - 1;
  assert.deepEqual(resolveJsonPathAtOffset(source, whitespaceOffset), { kind: 'none' });
  assert.deepEqual(resolveJsonPathAtOffset(source, 0), { kind: 'none' });
  assert.deepEqual(resolveJsonPathAtOffset(source, source.indexOf('{')), { kind: 'none' });
  const familyEnd = source.indexOf('"family"') + '"family"'.length;
  assert.deepEqual(resolveJsonPathAtOffset(source, familyEnd), { kind: 'none' });
});

test('incomplete json returns invalid', () => {
  assert.deepEqual(resolveJsonPathAtOffset('{"name": [', 9), { kind: 'invalid' });
});

test('negative and source length offsets return none', () => {
  assert.deepEqual(resolveJsonPathAtOffset(source, -1), { kind: 'none' });
  assert.deepEqual(resolveJsonPathAtOffset(source, source.length), { kind: 'none' });
});
