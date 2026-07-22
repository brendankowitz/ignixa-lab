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

test('every documented reserved word colors as a keyword (catches a future edit silently dropping one from the alternation)', () => {
  const keywords = [
    'WITH', 'AS', 'SELECT', 'DISTINCT', 'TOP', 'FROM', 'INNER', 'LEFT', 'RIGHT', 'OUTER', 'JOIN', 'ON',
    'AND', 'OR', 'NOT', 'WHERE', 'ORDER', 'BY', 'ASC', 'DESC', 'UNION', 'ALL', 'EXISTS', 'CASE', 'WHEN',
    'THEN', 'ELSE', 'END', 'NULL', 'IS', 'IN', 'LIKE', 'OVER', 'COLLATE', 'CAST', 'BIT',
  ];
  for (const keyword of keywords) {
    const [token] = tokenizeSql(keyword);
    assert.equal(token.color, 'var(--accent)', `expected "${keyword}" to color as a keyword`);
  }
});

test('a CASE/WHEN/THEN/ELSE/END conditional (as emitted for the include-limit IsPartial flag) colors every branch keyword', () => {
  const tokens = tokenizeSql('CASE WHEN COUNT_BIG(*) OVER() > 0 THEN 1 ELSE 0 END');
  for (const keyword of ['CASE', 'WHEN', 'THEN', 'ELSE', 'END']) {
    const token = tokens.find((t) => t.text === keyword);
    assert.equal(token?.color, 'var(--accent)', `expected "${keyword}" to color as a keyword`);
  }
  const fnToken = tokens.find((t) => t.text === 'COUNT_BIG');
  assert.equal(fnToken?.color, 'var(--chip-ind-fg)');
});

test('the ISNULL(..., N\'\') sentinel emitted for a nullable multi-key _sort tokenizes each piece distinctly', () => {
  const tokens = tokenizeSql("ISNULL(sk1.Text, N'')");
  const isnull = tokens.find((t) => t.text === 'ISNULL');
  assert.equal(isnull?.color, 'var(--chip-ind-fg)', 'ISNULL is a function call, not a listed keyword');
  const nPrefix = tokens.find((t) => t.text === 'N');
  assert.equal(nPrefix?.color, 'var(--text)', 'the N prefix on N\'\' is a plain identifier, not a keyword or function');
  const emptyString = tokens.find((t) => t.text === "''");
  assert.equal(emptyString?.color, 'var(--chip-teal-fg)', 'the empty string literal is its own token, immediately after N');
});

test('tokenizing reconstructs the original text exactly (no characters dropped or reordered)', () => {
  const sql = "cte0 AS (\n    SELECT DISTINCT r.T1, r.Sid1\n    FROM dbo.StringSearchParam ssp\n    WHERE ssp.Text LIKE @p0\n)";
  const rebuilt = tokenizeSql(sql)
    .map((t) => t.text)
    .join('');
  assert.equal(rebuilt, sql);
});
