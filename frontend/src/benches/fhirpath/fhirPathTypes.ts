export type FhirVersion = 'stu3' | 'r4' | 'r4b' | 'r5' | 'r6';

export interface FpVariable {
  name: string;
  value: string;
}

export interface FpResultItem {
  type: string;
  text: string;
}

export interface FpResultGroup {
  label: string | null;
  items: FpResultItem[];
}

export interface FpTraceRow {
  label: string;
  items: FpResultItem[];
}

export interface FpAstNode {
  expressionType: string;
  name: string;
  returnType: string | null;
  arguments: FpAstNode[];
  /** Character offset of this node's source span in the expression text, when the backend's parser recorded a location for it (synthetic nodes like the implicit expression scope have none). */
  position?: number;
  /** Length in characters of this node's source span. */
  length?: number;
  line?: number;
  column?: number;
}

export interface FpEvalResult {
  error: string | null;
  evaluator: string;
  groups: FpResultGroup[];
  trace: FpTraceRow[];
  /** `null` before any evaluation has produced an AST; `'parse-failed'` if the backend sent one but it couldn't be parsed. */
  ast: FpAstNode | null | 'parse-failed';
}

/** A generic FHIR `Parameters.parameter[]` entry — permissive enough to cover every `value[x]`/`resource`/`part` shape the backend emits. */
export interface FhirParameter {
  name: string;
  part?: FhirParameter[];
  resource?: unknown;
  extension?: { url: string; valueString?: string }[];
  [valueKey: string]: unknown;
}

export interface FhirParameters {
  resourceType: 'Parameters';
  id?: string;
  parameter?: FhirParameter[];
}
