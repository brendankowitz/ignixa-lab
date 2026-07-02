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
}

export interface FpEvalResult {
  error: string | null;
  evaluator: string;
  groups: FpResultGroup[];
  trace: FpTraceRow[];
  ast: FpAstNode | null;
  astParseFailed: boolean;
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
