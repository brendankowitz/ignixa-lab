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

/** `dataType` is the parameter's own resolved FHIR search-parameter type (e.g. "String", "Token", "Date",
 * "Reference", "Composite") — null when it never reached a successful parse (an Ignored/Failed outcome). */
export interface ParameterTrace {
  ordinal: number;
  key: string;
  value: string;
  keySyntax: SyntaxNode | null;
  valueSyntax: SyntaxNode | null;
  ir: IrRow[];
  dataType: string | null;
  outcome: ParameterOutcome;
}

/** `kind` is the row's underlying `Ignixa.Search.Sql.Ast.CteDefinition` case name (ParamSource,
 * Intersect, Union, Except, ChainJoin, CompartmentSource, ResourceSource) for a CTE row, or a
 * result-shape modifier's own name (Sort, Page, Include, CountOnly) for a non-CTE row. `cteIndex` is
 * this row's real position in `QueryPlan.ctes` (null for a non-CTE row) — the backend's own `PlanExplainer`
 * relabels the plan's match/output CTE as `"root"` instead of `"cte{i}"`, so `label` alone can't always
 * find this row's provenance entry. */
export interface PlanExplainRow {
  label: string;
  kind: string;
  cteIndex: number | null;
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

/** Matches `SearchEngineFactory`'s supported set exactly — the same versions `FhirPathBench` offers. */
export const FHIR_VERSIONS = ['STU3', 'R4', 'R4B', 'R5', 'R6'] as const;
export type FhirVersion = (typeof FHIR_VERSIONS)[number];

export const DEFAULT_FHIR_VERSION: FhirVersion = 'R4';
export const DEFAULT_RESOURCE_TYPE: ResourceType = 'Patient';
export const DEFAULT_QUERY = 'name=Smith&birthdate=gt2000-01-01';
