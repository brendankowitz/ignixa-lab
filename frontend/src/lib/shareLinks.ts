import type { TabId } from '../components/TopBar';
import type { FhirVersion as ConformanceFhirVersion } from '../hooks/useRunConfig';
import type { FhirVersion as FhirPathVersion, FpVariable } from '../benches/fhirpath/fhirPathTypes';
import type { SampleId } from '../benches/fhirpath/sampleResources';

export type BenchId = 'fhirpath' | 'fml' | 'sqlonfhir' | 'fakes';
export type FakesMode = 'population' | 'scenario' | 'resource';
export const COPY_FEEDBACK_DURATION_MS = 1400;

export interface ConformanceShareState {
  tab?: TabId;
  targetUrl?: string;
  fhirVersion?: ConformanceFhirVersion;
  suiteIds?: string[];
}

export interface FhirPathShareState {
  version?: FhirPathVersion;
  expression?: string;
  context?: string;
  sampleId?: SampleId;
  resourceText?: string;
  variables?: FpVariable[];
}

export interface FakesShareState {
  mode?: FakesMode;
  fhirVersion?: string;
  population?: {
    source?: string;
    count?: number;
    format?: 'transaction' | 'ndjson';
  };
  scenario?: {
    scenarioId?: string;
    paramValues?: Record<string, unknown>;
    tag?: string;
    resolvedReferences?: boolean;
  };
  resource?: {
    resourceType?: string;
    density?: string;
    seed?: number;
    randomizeSeed?: boolean;
    observationState?: string;
    firstName?: string;
    familyName?: string;
    city?: string;
    edgeCaseOn?: boolean;
    includeInvalid?: boolean;
    selectedCategories?: Record<string, boolean>;
  };
}

export interface BenchShareState {
  fhirpath?: FhirPathShareState;
  fakes?: FakesShareState;
}

const CONFORMANCE_FHIR_VERSIONS: ConformanceFhirVersion[] = ['R4', 'R4B', 'R5', 'STU3'];
// Runner/Report show live run/result state that a fresh page load never has,
// so a deep link can only ever restore the Setup tab. buildConformanceShareUrl
// still encodes whatever tab the user is on when they copy the link.
const CONFORMANCE_TABS: TabId[] = ['setup'];
const BENCHES: BenchId[] = ['fhirpath', 'fml', 'sqlonfhir', 'fakes'];
const FHIRPATH_VERSIONS: FhirPathVersion[] = ['stu3', 'r4', 'r4b', 'r5', 'r6'];
const SAMPLE_IDS: SampleId[] = ['patient', 'observation', 'custom'];
const FAKES_MODES: FakesMode[] = ['population', 'scenario', 'resource'];
const POPULATION_FORMATS: NonNullable<NonNullable<FakesShareState['population']>['format']>[] = ['transaction', 'ndjson'];
const DENSITY_VALUES: string[] = ['Minimal', 'Maximum'];

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function encodeShareState(value: unknown): string {
  const bytes = new TextEncoder().encode(JSON.stringify(value));
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
}

export function decodeShareState(value: string | null): unknown {
  if (!value) {
    return null;
  }
  try {
    const base64 = value.replaceAll('-', '+').replaceAll('_', '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const binary = atob(padded);
    const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
    return JSON.parse(new TextDecoder().decode(bytes)) as unknown;
  } catch {
    return null;
  }
}

export function readConformanceShareState(): ConformanceShareState {
  const params = new URLSearchParams(window.location.search);
  const decoded = decodeShareState(params.get('state'));
  const state: ConformanceShareState = isRecord(decoded) ? conformanceStateFromRecord(decoded) : {};
  const tab = params.get('tab');
  const fhirVersion = params.get('fhirVersion');
  const suiteIds = params.get('suites');
  const targetUrl = params.get('url');

  if (CONFORMANCE_TABS.includes(tab as TabId)) {
    state.tab = tab as TabId;
  }
  if (CONFORMANCE_FHIR_VERSIONS.includes(fhirVersion as ConformanceFhirVersion)) {
    state.fhirVersion = fhirVersion as ConformanceFhirVersion;
  }
  if (targetUrl) {
    state.targetUrl = targetUrl;
  }
  if (suiteIds) {
    state.suiteIds = suiteIds.split(',').map((id) => id.trim()).filter(Boolean);
  }
  return state;
}

export function buildConformanceShareUrl(state: ConformanceShareState): string {
  const url = new URL(window.location.href);
  url.search = '';
  url.hash = '';
  if (state.tab) {
    url.searchParams.set('tab', state.tab);
  }
  if (state.targetUrl) {
    url.searchParams.set('url', state.targetUrl);
  }
  if (state.fhirVersion) {
    url.searchParams.set('fhirVersion', state.fhirVersion);
  }
  if (state.suiteIds && state.suiteIds.length > 0) {
    url.searchParams.set('suites', state.suiteIds.join(','));
  }
  return url.toString();
}

export function readBenchShare(): { bench?: BenchId; state: BenchShareState } {
  const params = new URLSearchParams(window.location.search);
  const bench = params.get('bench');
  const decoded = decodeShareState(params.get('state'));
  return {
    bench: BENCHES.includes(bench as BenchId) ? (bench as BenchId) : undefined,
    state: isRecord(decoded) ? benchStateFromRecord(decoded) : {},
  };
}

export function buildBenchShareUrl(bench: BenchId, state: BenchShareState): string {
  const url = new URL(window.location.href);
  url.search = '';
  url.hash = '';
  url.searchParams.set('bench', bench);
  url.searchParams.set('state', encodeShareState(state));
  return url.toString();
}

function conformanceStateFromRecord(record: Record<string, unknown>): ConformanceShareState {
  return {
    tab: CONFORMANCE_TABS.includes(record.tab as TabId) ? (record.tab as TabId) : undefined,
    targetUrl: typeof record.targetUrl === 'string' ? record.targetUrl : undefined,
    fhirVersion: CONFORMANCE_FHIR_VERSIONS.includes(record.fhirVersion as ConformanceFhirVersion)
      ? (record.fhirVersion as ConformanceFhirVersion)
      : undefined,
    suiteIds: Array.isArray(record.suiteIds) ? record.suiteIds.filter((id): id is string => typeof id === 'string') : undefined,
  };
}

function benchStateFromRecord(record: Record<string, unknown>): BenchShareState {
  return {
    fhirpath: isRecord(record.fhirpath) ? fhirPathStateFromRecord(record.fhirpath) : undefined,
    fakes: isRecord(record.fakes) ? fakesStateFromRecord(record.fakes) : undefined,
  };
}

function fhirPathStateFromRecord(record: Record<string, unknown>): FhirPathShareState {
  return {
    version: FHIRPATH_VERSIONS.includes(record.version as FhirPathVersion) ? (record.version as FhirPathVersion) : undefined,
    expression: typeof record.expression === 'string' ? record.expression : undefined,
    context: typeof record.context === 'string' ? record.context : undefined,
    sampleId: SAMPLE_IDS.includes(record.sampleId as SampleId) ? (record.sampleId as SampleId) : undefined,
    resourceText: typeof record.resourceText === 'string' ? record.resourceText : undefined,
    variables: Array.isArray(record.variables)
      ? record.variables
          .filter(isRecord)
          .map((variable) => ({
            name: typeof variable.name === 'string' ? variable.name : '',
            value: typeof variable.value === 'string' ? variable.value : '',
          }))
      : undefined,
  };
}

function fakesStateFromRecord(record: Record<string, unknown>): FakesShareState {
  return {
    mode: FAKES_MODES.includes(record.mode as FakesMode) ? (record.mode as FakesMode) : undefined,
    fhirVersion: typeof record.fhirVersion === 'string' ? record.fhirVersion : undefined,
    population: isRecord(record.population) ? populationStateFromRecord(record.population) : undefined,
    scenario: isRecord(record.scenario) ? scenarioStateFromRecord(record.scenario) : undefined,
    resource: isRecord(record.resource) ? resourceStateFromRecord(record.resource) : undefined,
  };
}

function populationStateFromRecord(record: Record<string, unknown>): NonNullable<FakesShareState['population']> {
  return {
    source: typeof record.source === 'string' ? record.source : undefined,
    count: typeof record.count === 'number' && Number.isInteger(record.count) && record.count >= 1 && record.count <= 10 ? record.count : undefined,
    format: POPULATION_FORMATS.includes(record.format as NonNullable<NonNullable<FakesShareState['population']>['format']>)
      ? (record.format as NonNullable<NonNullable<FakesShareState['population']>['format']>)
      : undefined,
  };
}

function scenarioStateFromRecord(record: Record<string, unknown>): NonNullable<FakesShareState['scenario']> {
  return {
    scenarioId: typeof record.scenarioId === 'string' ? record.scenarioId : undefined,
    paramValues: isRecord(record.paramValues) ? record.paramValues : undefined,
    tag: typeof record.tag === 'string' ? record.tag : undefined,
    resolvedReferences: typeof record.resolvedReferences === 'boolean' ? record.resolvedReferences : undefined,
  };
}

function resourceStateFromRecord(record: Record<string, unknown>): NonNullable<FakesShareState['resource']> {
  return {
    resourceType: typeof record.resourceType === 'string' ? record.resourceType : undefined,
    density: DENSITY_VALUES.includes(record.density as string) ? (record.density as string) : undefined,
    seed: typeof record.seed === 'number' && Number.isFinite(record.seed) && Number.isInteger(record.seed) ? record.seed : undefined,
    randomizeSeed: typeof record.randomizeSeed === 'boolean' ? record.randomizeSeed : undefined,
    observationState: typeof record.observationState === 'string' ? record.observationState : undefined,
    firstName: typeof record.firstName === 'string' ? record.firstName : undefined,
    familyName: typeof record.familyName === 'string' ? record.familyName : undefined,
    city: typeof record.city === 'string' ? record.city : undefined,
    edgeCaseOn: typeof record.edgeCaseOn === 'boolean' ? record.edgeCaseOn : undefined,
    includeInvalid: typeof record.includeInvalid === 'boolean' ? record.includeInvalid : undefined,
    selectedCategories: isRecord(record.selectedCategories) ? stringBooleanRecord(record.selectedCategories) : undefined,
  };
}

function stringBooleanRecord(record: Record<string, unknown>): Record<string, boolean> {
  const result: Record<string, boolean> = {};
  for (const [key, value] of Object.entries(record)) {
    if (typeof value === 'boolean') {
      result[key] = value;
    }
  }
  return result;
}
