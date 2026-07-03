import type { FpAstNode } from './fhirPathTypes';

/**
 * Flattens an AST's primary "focus" chain (each step's `arguments[0]`, the
 * left-hand-side it was applied to) into a linear top-level sequence in
 * source order — e.g. `Patient.name.given.first()` nests `.first()` around
 * `.given` around `.name` around `Patient` in the normal tree, but reads
 * `Patient` → `.name` → `.given` → `.first()` here, matching the expression
 * text left-to-right. Any *other* arguments (a function's non-focus args, an
 * operator's right-hand side) are inverted too and become that step's
 * children instead of staying nested in the chain. Same idea as fhirpath-lab's
 * "Inverted Tree" toggle.
 */
export function invertAstTree(node: FpAstNode): FpAstNode[] {
  const rootItem: FpAstNode = {
    expressionType: node.expressionType,
    name: node.name,
    returnType: node.returnType,
    arguments: [],
    position: node.position,
    length: node.length,
    line: node.line,
    column: node.column,
  };

  const result: FpAstNode[] = [];
  if (node.arguments.length > 0) {
    result.push(...invertAstTree(node.arguments[0]));
    for (let i = 1; i < node.arguments.length; i++) {
      rootItem.arguments.push(...invertAstTree(node.arguments[i]));
    }
  }
  result.push(rootItem);
  return result;
}
