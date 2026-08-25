/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULT_EXPRESSION, EXAMPLE_EXPRESSIONS } from './sampleResources.ts';

test('instance-selector examples are available for their matching resources', () => {
  assert.ok(EXAMPLE_EXPRESSIONS.patient.includes("Reference { reference: 'Patient/' & id, display: name.first().given.first() & ' ' & name.first().family }"));
  assert.ok(EXAMPLE_EXPRESSIONS.observation.includes('code.coding.select(CodeableConcept { coding: $this, text: display })'));
  assert.ok(EXAMPLE_EXPRESSIONS.observation.includes("Annotation { text: 'BP ' & component.value.value.first().toString() & '/' & component.value.value.last().toString() & ' mmHg' }"));
  assert.ok(EXAMPLE_EXPRESSIONS.custom.includes("CodeableConcept { coding: Coding { system: 'http://loinc.org', code: '8480-6' }, text: 'Systolic BP' }"));
});

test('default expression remains the first patient example', () => {
  assert.equal(DEFAULT_EXPRESSION, "name.where(use = 'official').given.first()");
});
