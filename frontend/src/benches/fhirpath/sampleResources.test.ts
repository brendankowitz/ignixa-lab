/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULT_EXPRESSION, EXAMPLE_EXPRESSIONS } from './sampleResources.ts';

test('instance-selector examples are available for their matching resources', () => {
  assert.ok(EXAMPLE_EXPRESSIONS.patient.includes('name.select(HumanName { family: family, given: given.first() })'));
  assert.ok(EXAMPLE_EXPRESSIONS.observation.includes('component.code.coding.select(Coding { system: system, code: code })'));
  assert.ok(EXAMPLE_EXPRESSIONS.custom.includes("Coding { system: 'http://loinc.org', code: '8480-6' }"));
});

test('default expression remains the first patient example', () => {
  assert.equal(DEFAULT_EXPRESSION, "name.where(use = 'official').given.first()");
});
