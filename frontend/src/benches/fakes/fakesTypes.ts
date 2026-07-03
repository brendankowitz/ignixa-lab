export interface ScenarioParameterMetadata {
  name: string;
  type: string;
  defaultValue: unknown;
}

export interface ScenarioMetadata {
  id: string;
  parameters: ScenarioParameterMetadata[];
}

export interface EdgeCaseCategoryMetadata {
  id: string;
  intent: 'PreservesValidity' | 'MayViolate' | 'AlwaysInvalid';
}

export interface EdgeCaseFamilyMetadata {
  family: string;
  categories: EdgeCaseCategoryMetadata[];
}

export interface FakesMetadata {
  /** The referenced Ignixa.FhirFakes package version (e.g. "0.5.13"), reflected server-side from the assembly. */
  libraryVersion: string;
  fhirVersions: string[];
  populationStates: string[];
  scenarios: ScenarioMetadata[];
  /** Valid FHIR resource type names, keyed by lowercase FHIR version — differs between versions (e.g. R6 added/dropped types vs R4). */
  resourceTypesByVersion: Record<string, string[]>;
  observationStates: string[];
  edgeCaseFamilies: EdgeCaseFamilyMetadata[];
  /** City names the backend can sample realistic demographics (including gender) from for a generated Patient. */
  patientCities: string[];
}

export interface PopulationSummary {
  byType: Record<string, number>;
  byGender: Record<string, number>;
  byCity: Record<string, number>;
  ageBuckets: Record<string, number>;
}

export interface PopulationResult {
  patients: Record<string, unknown>[];
  resources: Record<string, unknown>[];
  summary: PopulationSummary;
}

export interface ScenarioResult {
  patient: Record<string, unknown> | null;
  resources: Record<string, unknown>[];
  bundle: Record<string, unknown>;
}

export interface MutationRecord {
  category: string;
  path: string;
  before: string | null;
  after: string | null;
  description: string;
}

export interface MutationManifest {
  resourceId: string;
  seed: number;
  mutations: MutationRecord[];
}

export interface ResourceResult {
  resource: Record<string, unknown>;
  manifest: MutationManifest | null;
}
