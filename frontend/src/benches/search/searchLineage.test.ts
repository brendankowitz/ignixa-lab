/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { ordinalForCteLabel, isRowSelected, isRangeSelected } from './searchLineage.ts';
import type { QueryPlan } from './searchTypes.ts';

const plan: QueryPlan = {
  explain: '',
  rows: [
    { label: 'root', body: 'Resource Patient' },
    { label: 'cte0', body: 'ParamSource name' },
    { label: 'cte1', body: 'ParamSource gender' },
  ],
  ctes: [
    { cteIndex: 0, parameterOrdinal: 0, span: null },
    { cteIndex: 1, parameterOrdinal: 1, span: null },
  ],
};

test('a cte label resolves to its owning parameter ordinal', () => {
  assert.equal(ordinalForCteLabel(plan, 'cte1'), 1);
});

test('a non-cte label resolves to null', () => {
  assert.equal(ordinalForCteLabel(plan, 'root'), null);
});

test('a plan row is selected when its cte maps to the selected ordinal', () => {
  assert.equal(isRowSelected('cte0', 0, plan), true);
  assert.equal(isRowSelected('cte1', 0, plan), false);
  assert.equal(isRowSelected('root', 0, plan), false);
});

test('nothing is selected when selectedOrdinal is null', () => {
  assert.equal(isRowSelected('cte0', null, plan), false);
  assert.equal(isRangeSelected('cte0', null, plan), false);
});

test('a sql range is selected when its cte maps to the selected ordinal', () => {
  assert.equal(isRangeSelected('cte1', 1, plan), true);
  assert.equal(isRangeSelected('cte1', 0, plan), false);
});
