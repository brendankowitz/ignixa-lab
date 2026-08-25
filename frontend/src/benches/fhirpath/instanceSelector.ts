import type { FpAstNode } from './fhirPathTypes';

export function hasInstanceSelector(ast: FpAstNode | null | 'parse-failed'): boolean {
  if (ast === null || ast === 'parse-failed') {
    return false;
  }

  return ast.expressionType === 'InstanceSelectorExpression' || ast.arguments.some(hasInstanceSelector);
}
