/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { invertAstTree } from './astInvert.ts';
import { parseFhirPathResponse } from './fhirPathApi.ts';
import type { FhirParameters } from './fhirPathTypes.ts';

test('parseFhirPathResponse preserves nested instance-selector AST nodes', () => {
  const response: FhirParameters = {
    resourceType: 'Parameters',
    parameter: [
      {
        name: 'parameters',
        part: [
          {
            name: 'parseDebugTree',
            valueString: JSON.stringify({
              ExpressionType: 'InstanceSelectorExpression',
              Name: 'FHIR.Identifier',
              ReturnType: 'Identifier',
              TypeName: 'Identifier',
              NamespacePrefix: 'FHIR',
              IsEmpty: false,
              Arguments: [
                {
                  ExpressionType: 'ElementAssignment',
                  Name: 'system',
                  ReturnType: 'string',
                  Arguments: [
                    {
                      ExpressionType: 'ConstantExpression',
                      Name: 'http://example.org',
                      ReturnType: 'string',
                      Arguments: [],
                    },
                  ],
                },
              ],
            }),
          },
        ],
      },
    ],
  };

  const ast = parseFhirPathResponse(response).ast;

  if (ast === null || ast === 'parse-failed') {
    assert.fail('expected a parsed AST');
  }
  assert.equal(ast.expressionType, 'InstanceSelectorExpression');
  assert.equal(ast.typeName, 'Identifier');
  assert.equal(ast.namespacePrefix, 'FHIR');
  assert.equal(ast.isEmpty, false);
  assert.equal(ast.arguments[0]?.expressionType, 'ElementAssignment');
  assert.equal(ast.arguments[0]?.name, 'system');
  assert.equal(ast.arguments[0]?.arguments[0]?.name, 'http://example.org');

  const inverted = invertAstTree(ast);
  assert.equal(inverted.length, 1);
  const invertedSelector = inverted[0];
  assert.equal(invertedSelector?.typeName, 'Identifier');
  assert.equal(invertedSelector?.namespacePrefix, 'FHIR');
  assert.equal(invertedSelector?.isEmpty, false);
  assert.equal(invertedSelector?.arguments.length, 1);
  assert.equal(invertedSelector?.arguments[0]?.expressionType, 'ElementAssignment');
  assert.equal(invertedSelector?.arguments[0]?.arguments[0]?.expressionType, 'ConstantExpression');
});
