/** Mirrors backend Models/Search/SearchTraceResponse.cs (serialized camelCase). */

export type SourceOrigin = 'Key' | 'Value';

/** A range within one parameter's key or value string (per `origin`) — NOT the whole query string. */
export interface Span {
  origin: SourceOrigin;
  start: number;
  length: number;
}

export interface SyntaxNode {
  kind: string;
  span: Span;
  children: SyntaxNode[];
}

export interface IrRow {
  kind: string;
  text: string;
  depth: number;
}

export type OutcomeKind = 'Compiled' | 'Ignored' | 'Failed';

export interface ParameterOutcome {
  kind: OutcomeKind;
  reason: string | null;
  stage: string | null;
  span: Span | null;
}

export interface ParameterTrace {
  ordinal: number;
  key: string;
  value: string;
  keySyntax: SyntaxNode | null;
  valueSyntax: SyntaxNode | null;
  ir: IrRow[];
  outcome: ParameterOutcome;
}

export interface PlanExplainRow {
  label: string;
  body: string;
}

export interface CteProvenance {
  cteIndex: number;
  parameterOrdinal: number | null;
  span: Span | null;
}

export interface QueryPlan {
  explain: string;
  rows: PlanExplainRow[];
  ctes: CteProvenance[];
}

export interface SqlTextRange {
  label: string;
  start: number;
  length: number;
}

export interface EmittedSql {
  sql: string;
  ranges: SqlTextRange[];
}

export interface ImplicitParameter {
  name: string;
  value: string;
  reason: string;
}

export interface TraceFailure {
  stage: string;
  message: string;
  span: Span | null;
}

export interface SearchTraceResponse {
  resourceType: string;
  parameters: ParameterTrace[];
  plan: QueryPlan | null;
  sql: EmittedSql | null;
  implicit: ImplicitParameter[];
  failure: TraceFailure | null;
}

export const RESOURCE_TYPES = ['Patient', 'Observation', 'Encounter'] as const;
export type ResourceType = (typeof RESOURCE_TYPES)[number];

export const DEFAULT_RESOURCE_TYPE: ResourceType = 'Patient';
export const DEFAULT_QUERY = 'name=Smith&birthdate=gt2000-01-01';

/** Example query chips shown under the expression input (mirrors the mock's example chips). */
export const EXAMPLE_QUERIES: Record<ResourceType, string[]> = {
  Patient: ['name=Smith&birthdate=gt2000-01-01', 'general-practitioner.name=Jones', 'gender=male&_sort=name'],
  Observation: ['code=http://loinc.org|8480-6', 'code-value-quantity=8480-6$gt90', 'patient=Patient/123'],
  Encounter: ['status=finished', 'date=ge2024-01-01', 'subject.name=Smith'],
};
