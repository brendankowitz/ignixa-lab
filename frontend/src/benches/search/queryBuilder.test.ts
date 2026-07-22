/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { parseQueryString, toQueryString, upsertSingleton, removeKey, appendUnique, removePair, setSort } from './queryBuilder.ts';

test('parseQueryString splits key=value pairs on &', () => {
  assert.deepEqual(parseQueryString('name=Smith&gender=male'), [
    { key: 'name', value: 'Smith' },
    { key: 'gender', value: 'male' },
  ]);
});

test('parseQueryString tolerates a leading ?, trailing &, and blank segments', () => {
  assert.deepEqual(parseQueryString('?name=Smith&&gender=male&'), [
    { key: 'name', value: 'Smith' },
    { key: 'gender', value: 'male' },
  ]);
});

test('parseQueryString on an empty/whitespace string returns no pairs', () => {
  assert.deepEqual(parseQueryString(''), []);
  assert.deepEqual(parseQueryString('   '), []);
});

test('parseQueryString treats a key with no "=" as an empty value, not a dropped pair', () => {
  assert.deepEqual(parseQueryString('_summary'), [{ key: '_summary', value: '', bare: true }]);
});

test('toQueryString is the inverse of parseQueryString for well-formed input', () => {
  const query = 'name=Smith&gender=male';
  assert.equal(toQueryString(parseQueryString(query)), query);
});

test('toQueryString round-trips a bare key (no "=") unchanged, rather than silently appending "="', () => {
  assert.equal(toQueryString(parseQueryString('_summary')), '_summary');
  assert.equal(toQueryString(parseQueryString('name=Smith&_summary&gender=male')), 'name=Smith&_summary&gender=male');
});

test('a bare key elsewhere in the query survives an unrelated builder edit unchanged', () => {
  // Regression: upsertSingleton/removeKey/appendUnique only filter the pairs array by key -- a pair they
  // don't touch must come back through toQueryString exactly as it was typed, not gain a "=" it never had.
  assert.equal(upsertSingleton('name=Smith&_summary', '_total', 'accurate'), 'name=Smith&_summary&_total=accurate');
  assert.equal(appendUnique('_summary&name=Smith', '_include', 'Patient:general-practitioner'), '_summary&name=Smith&_include=Patient:general-practitioner');
});

test('upsertSingleton appends a new key on an empty query', () => {
  assert.equal(upsertSingleton('', '_summary', 'count'), '_summary=count');
});

test('upsertSingleton appends a new key alongside existing pairs', () => {
  assert.equal(upsertSingleton('name=Smith', '_summary', 'count'), 'name=Smith&_summary=count');
});

test('upsertSingleton replaces an existing value for the same key in place, not appending a duplicate', () => {
  assert.equal(upsertSingleton('name=Smith&_summary=true', '_summary', 'count'), 'name=Smith&_summary=count');
});

test('upsertSingleton with a blank value removes the key instead of adding key=', () => {
  assert.equal(upsertSingleton('name=Smith&_summary=count', '_summary', ''), 'name=Smith');
});

test('removeKey drops only the matching key, leaving other pairs untouched', () => {
  assert.equal(removeKey('name=Smith&_summary=count&gender=male', '_summary'), 'name=Smith&gender=male');
});

test('removeKey on a query without that key is a no-op', () => {
  assert.equal(removeKey('name=Smith', '_summary'), 'name=Smith');
});

test('appendUnique adds a new repeatable pair', () => {
  assert.equal(appendUnique('name=Smith', '_include', 'Patient:general-practitioner'), 'name=Smith&_include=Patient:general-practitioner');
});

test('appendUnique adding the identical key=value pair twice is a no-op', () => {
  const once = appendUnique('', '_include', 'Patient:general-practitioner');
  assert.equal(appendUnique(once, '_include', 'Patient:general-practitioner'), once);
});

test('appendUnique allows the same key with a DIFFERENT value to coexist', () => {
  const once = appendUnique('', '_include', 'Patient:general-practitioner');
  assert.equal(appendUnique(once, '_include', 'Patient:organization'), '_include=Patient:general-practitioner&_include=Patient:organization');
});

test('removePair removes only the exact key=value pair, not every pair sharing that key', () => {
  const query = '_include=Patient:general-practitioner&_include=Patient:organization';
  assert.equal(removePair(query, '_include', 'Patient:organization'), '_include=Patient:general-practitioner');
});

test('setSort with no fields removes _sort entirely rather than adding an empty value', () => {
  assert.equal(setSort('name=Smith&_sort=name', []), 'name=Smith');
});

test('setSort builds a comma-joined value with "-" prefixing descending fields', () => {
  assert.equal(
    setSort('name=Smith', [
      { field: 'birthdate', descending: true },
      { field: 'name', descending: false },
    ]),
    'name=Smith&_sort=-birthdate,name',
  );
});

test('setSort replaces an existing _sort value rather than appending a second _sort key', () => {
  assert.equal(setSort('_sort=name', [{ field: 'birthdate', descending: false }]), '_sort=birthdate');
});

test('setSort drops blank field slots (an unfilled builder picker) without emitting a stray comma', () => {
  assert.equal(
    setSort('', [
      { field: 'name', descending: false },
      { field: '', descending: false },
      { field: 'birthdate', descending: true },
    ]),
    '_sort=name,-birthdate',
  );
});
