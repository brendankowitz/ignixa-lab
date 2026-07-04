import type { FakesMetadata, PopulationResult, ResourceResult, ScenarioResult } from './fakesTypes';

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });

  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as { error?: string } | null;
    throw new Error(body?.error ?? `Request failed with status ${response.status}`);
  }

  return (await response.json()) as T;
}

export function getFakesMetadata(signal?: AbortSignal): Promise<FakesMetadata> {
  return request<FakesMetadata>('/api/fakes/metadata', { signal });
}

export function generatePopulation(
  body: { fhirVersion: string; source: string; count: number },
  signal?: AbortSignal,
): Promise<PopulationResult> {
  return request<PopulationResult>('/api/fakes/population', { method: 'POST', body: JSON.stringify(body), signal });
}

export function generateScenario(
  body: {
    fhirVersion: string;
    scenarioId: string;
    parameters?: Record<string, unknown>;
    tag?: string;
    resolvedReferences: boolean;
  },
  signal?: AbortSignal,
): Promise<ScenarioResult> {
  return request<ScenarioResult>('/api/fakes/scenario', { method: 'POST', body: JSON.stringify(body), signal });
}

export function generateResource(
  body: {
    fhirVersion: string;
    resourceType: string;
    seed: number;
    density: string;
    firstName?: string;
    familyName?: string;
    city?: string;
    observationState?: string;
    edgeCaseSelectors?: string[];
    includeInvalid: boolean;
  },
  signal?: AbortSignal,
): Promise<ResourceResult> {
  return request<ResourceResult>('/api/fakes/resource', { method: 'POST', body: JSON.stringify(body), signal });
}
