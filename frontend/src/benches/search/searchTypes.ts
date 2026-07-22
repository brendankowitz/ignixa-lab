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

/** Mirrors `Ignixa.Search.Sql.Ast.PlanExplainRow`. `label` is display text only (the match/output CTE
 * prints as `"root"`); `canonicalLabel` is the identifier this row shares with its `SqlTextRange` and
 * `CteProvenance` — join on that, never on `label`. `kind` is the row's `PlanRowKind` token (e.g.
 * `"intersect"`, `"chainJoin"`, `"includeStage"`, `"sortSpec"`). `referencedCteIndexes` lists the CTEs a
 * structural row (Intersect/Union/Except/ChainJoin) composes, in the order it names them. */
export interface PlanExplainRow {
  label: string;
  canonicalLabel: string;
  kind: string;
  body: string;
  referencedCteIndexes: number[];
}

/** `contributingOrdinals` is every parameter ordinal this CTE draws from — itself alone when
 * `parameterOrdinal` is set, or the closed-over union of its children's sets for a structural CTE
 * (Intersect/Union/Except/ChainJoin), or empty where nothing is attributable. */
export interface CteProvenance {
  cteIndex: number;
  parameterOrdinal: number | null;
  contributingOrdinals: number[];
  span: Span | null;
}

export interface QueryPlan {
  explain: string;
  rows: PlanExplainRow[];
  ctes: CteProvenance[];
}

/** `label` says which section this is (unique within one emitted statement) and, where a `PlanExplainRow`
 * exists for it, equals that row's `canonicalLabel`. `kind` is the row's `SqlRangeKind` token — set for
 * every range, including the structural ones with no row at all (`matchPage`/`where`/`seek`/`orderBy`/
 * `assembly`), so those spans are self-describing even though they can't be joined to a plan row. */
export interface SqlTextRange {
  label: string;
  kind: string;
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
