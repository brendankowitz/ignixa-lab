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
              Position: 0,
              Length: 63,
              Line: 1,
              Column: 0,
              Arguments: [
                {
                  ExpressionType: 'ElementAssignment',
                  Name: 'system',
                  ReturnType: 'string',
                  Position: 20,
                  Length: 20,
                  Line: 1,
                  Column: 20,
                  Arguments: [
                    {
                      ExpressionType: 'ConstantExpression',
                      Name: 'http://example.org',
                      ReturnType: 'string',
                      Position: 28,
                      Length: 20,
                      Line: 1,
                      Column: 28,
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
  assert.equal(ast.position, 0);
  assert.equal(ast.length, 63);
  assert.equal(ast.line, 1);
  assert.equal(ast.column, 0);
  assert.equal(ast.arguments[0]?.expressionType, 'ElementAssignment');
  assert.equal(ast.arguments[0]?.name, 'system');
  assert.equal(ast.arguments[0]?.arguments[0]?.name, 'http://example.org');
  assert.equal(ast.arguments[0]?.position, 20);
  assert.equal(ast.arguments[0]?.length, 20);
  assert.equal(ast.arguments[0]?.arguments[0]?.position, 28);
  assert.equal(ast.arguments[0]?.arguments[0]?.length, 20);

  const inverted = invertAstTree(ast);
  assert.equal(inverted.length, 1);
  const invertedSelector = inverted[0];
  assert.equal(invertedSelector?.typeName, 'Identifier');
  assert.equal(invertedSelector?.namespacePrefix, 'FHIR');
  assert.equal(invertedSelector?.isEmpty, false);
  assert.equal(invertedSelector?.position, 0);
  assert.equal(invertedSelector?.length, 63);
  assert.equal(invertedSelector?.arguments.length, 1);
  assert.equal(invertedSelector?.arguments[0]?.expressionType, 'ElementAssignment');
  assert.equal(invertedSelector?.arguments[0]?.arguments[0]?.expressionType, 'ConstantExpression');
  assert.equal(invertedSelector?.arguments[0]?.position, 20);
  assert.equal(invertedSelector?.arguments[0]?.arguments[0]?.position, 28);
});
