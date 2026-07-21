/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { tokenizeSql } from './sqlHighlight.ts';

function colorsOf(text: string): string[] {
  return tokenizeSql(text).map((t) => t.color);
}

test('reserved words are colored as keywords, case-insensitively', () => {
  const tokens = tokenizeSql('select Distinct top');
  assert.deepEqual(
    tokens.filter((t) => t.text.trim() !== '').map((t) => t.color),
    ['var(--accent)', 'var(--accent)', 'var(--accent)'],
  );
});

test('a single-quoted string literal is one token, colored distinctly from keywords', () => {
  const tokens = tokenizeSql("Text LIKE 'Smith'");
  const stringToken = tokens.find((t) => t.text === "'Smith'");
  assert.equal(stringToken?.color, 'var(--chip-teal-fg)');
});

test('an @parameter placeholder gets its own color', () => {
  const tokens = tokenizeSql('Text LIKE @p0');
  const paramToken = tokens.find((t) => t.text === '@p0');
  assert.equal(paramToken?.color, 'var(--chip-pink-fg)');
});

test('a number literal gets its own color, distinct from identifiers', () => {
  const tokens = tokenizeSql('TOP (10)');
  const numberToken = tokens.find((t) => t.text === '10');
  assert.equal(numberToken?.color, 'var(--chip-amb-fg)');
});

test('an identifier immediately followed by "(" is treated as a function call, distinct from a plain identifier', () => {
  const tokens = tokenizeSql('COUNT_BIG(*) OVER()');
  const fnToken = tokens.find((t) => t.text === 'COUNT_BIG');
  assert.equal(fnToken?.color, 'var(--chip-ind-fg)');
});

test('a plain table/column identifier falls back to the base text color', () => {
  const tokens = tokenizeSql('r.ResourceTypeId');
  const identifierTokens = tokens.filter((t) => t.text === 'r' || t.text === 'ResourceTypeId');
  assert.equal(identifierTokens.length, 2);
  for (const token of identifierTokens) {
    assert.equal(token.color, 'var(--text)');
  }
});

test('punctuation is muted rather than left at the identifier color', () => {
  assert.equal(colorsOf('.').at(0), 'var(--text2)');
  assert.equal(colorsOf('=').at(0), 'var(--text2)');
});

test('tokenizing reconstructs the original text exactly (no characters dropped or reordered)', () => {
  const sql = "cte0 AS (\n    SELECT DISTINCT r.T1, r.Sid1\n    FROM dbo.StringSearchParam ssp\n    WHERE ssp.Text LIKE @p0\n)";
  const rebuilt = tokenizeSql(sql)
    .map((t) => t.text)
    .join('');
  assert.equal(rebuilt, sql);
});
