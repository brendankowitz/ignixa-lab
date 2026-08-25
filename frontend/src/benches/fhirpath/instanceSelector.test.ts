/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { hasCurrentInstanceSelector, hasInstanceSelector } from './instanceSelector.ts';
import type { FpAstNode } from './fhirPathTypes.ts';

const nestedInstanceSelector: FpAstNode = {
  expressionType: 'FunctionCallExpression',
  name: 'select',
  returnType: 'Coding',
  arguments: [
    {
      expressionType: 'ChildExpression',
      name: 'coding',
      returnType: 'Coding',
      arguments: [],
    },
    {
      expressionType: 'InstanceSelectorExpression',
      name: 'FHIR.Identifier',
      returnType: 'Identifier',
      typeName: 'Identifier',
      namespacePrefix: 'FHIR',
      isEmpty: false,
      arguments: [
        {
          expressionType: 'ElementAssignment',
          name: 'system',
          returnType: 'string',
          arguments: [
            {
              expressionType: 'ConstantExpression',
              name: 'http://example.org',
              returnType: 'string',
              arguments: [],
            },
          ],
        },
      ],
    },
  ],
};

test('detects an instance selector nested in an evaluated AST', () => {
  assert.equal(hasInstanceSelector(nestedInstanceSelector), true);
});

test('does not detect instance selectors in a selector-free AST', () => {
  const ast: FpAstNode = {
    expressionType: 'ChildExpression',
    name: 'name',
    returnType: 'HumanName',
    arguments: [],
  };

  assert.equal(hasInstanceSelector(ast), false);
});

test('treats absent and unparseable ASTs as having no instance selector', () => {
  assert.equal(hasInstanceSelector(null), false);
  assert.equal(hasInstanceSelector('parse-failed'), false);
});

test('reports an instance selector only for the expression that produced its AST', () => {
  const evaluatedExpression = "Coding { system: 'http://loinc.org', code: '8480-6' }";

  assert.equal(hasCurrentInstanceSelector(nestedInstanceSelector, evaluatedExpression, evaluatedExpression), true);
  assert.equal(hasCurrentInstanceSelector(nestedInstanceSelector, 'name.given.first()', evaluatedExpression), false);
});
