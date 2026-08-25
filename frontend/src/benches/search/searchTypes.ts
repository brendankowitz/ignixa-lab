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

export type OutcomeKind = 'Compiled' | 'Ignored' | 'Failed' | 'KnownMiss';

export interface ParameterOutcome {
  kind: OutcomeKind;
  reason: string | null;
  stage: string | null;
  span: Span | null;
}

/** `dataType` is the parameter's own resolved FHIR search-parameter type (e.g. "String", "Token", "Date",
 * "Reference", "Composite") — null when it never reached a successful parse (an Ignored/Failed outcome).
 *
 * `irUnavailableReason` disambiguates an empty `ir`: null means the parameter genuinely has no IR, non-null
 * means the backend's projector could not describe the expression and says why. Render it — an empty pane
 * that silently means "couldn't say" is the one thing a provenance view must not show. */
export interface ParameterTrace {
  ordinal: number;
  key: string;
  value: string;
  keySyntax: SyntaxNode | null;
  valueSyntax: SyntaxNode | null;
  ir: IrRow[];
  dataType: string | null;
  outcome: ParameterOutcome;
  irUnavailableReason: string | null;
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

/** One bound parameter behind a `@pN` marker in `sql`. `value` is the backend's display rendering of the
 * bound value, null only when the value itself is null. */
export interface SqlParameter {
  name: string;
  value: string | null;
}

export interface EmittedSql {
  sql: string;
  parameters: SqlParameter[];
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
  parameterCode: string | null;
  span: Span | null;
}

/** `fhirVersion` is the version the backend actually compiled against, which is not always the one asked
 * for: an unrecognized value falls back to R4 rather than being rejected. Compare it against the requested
 * version and say so when they differ — that is the entire reason the backend sends it. */
export interface SearchTraceResponse {
  fhirVersion: string;
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

export type SearchMode = 'type' | 'compartment' | 'everything';

/** Which extra modes each resource type offers -- neither is "any of the bench's 3 resource types". Only
 * Patient and Encounter are among FHIR's 5 compartment types (Device, Encounter, Patient, Practitioner,
 * RelatedPerson), so only they get Compartment mode; Observation is a compartment member type, never a root.
 * $everything is offered for Patient alone: the backend exposes a Patient-anchored route only, so this table
 * must not offer the mode anywhere else. Adding a resource type to `RESOURCE_TYPES` fails to compile until
 * it is given an entry here, which is the point. */
const SEARCH_MODES_BY_RESOURCE_TYPE: Record<ResourceType, readonly SearchMode[]> = {
  Patient: ['type', 'compartment', 'everything'],
  Encounter: ['type', 'compartment'],
  Observation: ['type'],
};

export function searchModesFor(resourceType: ResourceType): readonly SearchMode[] {
  return SEARCH_MODES_BY_RESOURCE_TYPE[resourceType];
}

export type CompartmentMemberType = ResourceType | '*';

/** A resource type that is also a compartment root, i.e. one `compartmentMemberOptions` accepts. Derived
 * from {@link COMPARTMENT_MEMBERS} rather than declared, so the two can't drift. */
export type CompartmentRoot = keyof typeof COMPARTMENT_MEMBERS;

/** Which of the bench's resource types are actually members of each compartment root, mirroring the FHIR
 * CompartmentDefinitions the backend resolves against. Not derivable from `RESOURCE_TYPES` — membership is
 * asymmetric: a Patient is in its own compartment (via `link`), but a Patient is *not* in the Encounter
 * compartment, so offering it there is a guaranteed 400 from the backend's membership check.
 *
 * Duplicating the backend's source of truth is deliberate at this size (2 roots x 3 types) since there is no
 * metadata endpoint to read it from; `CompartmentMembershipContractTests` on the backend pins these exact
 * facts across all five supported FHIR versions so a package bump can't silently invalidate this table. If
 * the bench ever offers many more resource types, serve this from the API instead of growing the table. */
const COMPARTMENT_MEMBERS = {
  Patient: ['Patient', 'Observation', 'Encounter'],
  Encounter: ['Observation', 'Encounter'],
} as const satisfies Partial<Record<ResourceType, readonly ResourceType[]>>;

/** The member types searchable within `root`'s compartment, plus the `*` wildcard ("every type in the
 * compartment"). Every entry is a real member, so no option here can produce a membership 400. */
export function compartmentMemberOptions(root: CompartmentRoot): readonly CompartmentMemberType[] {
  return [...COMPARTMENT_MEMBERS[root], '*'];
}

/** Whether `root` has a compartment at all — the guard that makes `compartmentMemberOptions`'
 * narrower parameter type safe to reach from a plain `ResourceType`. */
export function isCompartmentRoot(resourceType: ResourceType): resourceType is CompartmentRoot {
  return resourceType in COMPARTMENT_MEMBERS;
}

/** The resource types offerable as $everything's `_type` filter. Patient is omitted because it is the
 * operation's anchor, so filtering to it alone is a degenerate case this bench doesn't model — not because
 * `_type` rejects it (FHIR permits `_type=Patient`, and the backend accepts any Patient-compartment
 * member). */
export function everythingTypeFilterOptions(): readonly ResourceType[] {
  return RESOURCE_TYPES.filter((type) => type !== 'Patient');
}

export type SearchRequest =
  | { mode: 'type'; fhirVersion: FhirVersion; resourceType: ResourceType; query: string }
  | {
      mode: 'compartment';
      fhirVersion: FhirVersion;
      // Not `ResourceType`: Observation has no compartment, so it can never be a root here.
      compartmentType: CompartmentRoot;
      compartmentId: string;
      memberType: CompartmentMemberType;
      query: string;
    }
  | {
      mode: 'everything';
      fhirVersion: FhirVersion;
      patientId: string;
      typeFilter: readonly ResourceType[];
      since: string;
      start: string;
      end: string;
      includeReferencedResources: boolean;
    };
