import { useState } from 'react';
import { useSuiteSelection, type SuiteSelection } from './useSuiteSelection';

/** FHIR versions offered by the Setup screen's version toggle. */
export const FHIR_VERSIONS = ['R4', 'R4B', 'R5', 'STU3'] as const;

/** A FHIR version selectable on the Setup screen. */
export type FhirVersion = (typeof FHIR_VERSIONS)[number];

/** Public shape returned by {@link useRunConfig}. */
export interface RunConfig {
  /** Bare host + path the user typed, without the `https://` prefix (e.g. `hapi.fhir.org/baseR4`). */
  endpoint: string;
  /** Updates the bare endpoint. */
  setEndpoint: (endpoint: string) => void;
  /** Full `https://` target URL, or an empty string until an endpoint is entered. */
  targetUrl: string;
  /** Selected FHIR version. */
  fhirVersion: FhirVersion;
  /** Updates the selected FHIR version. */
  setFhirVersion: (version: FhirVersion) => void;
  /** Suite selection state, shared with the Setup screen's suite checklist. */
  selection: SuiteSelection;
}

/**
 * Owns the Setup screen's run configuration: target endpoint, FHIR version,
 * and selected suite IDs. Deliberately holds no auth field — `RunRequest`
 * carries none, so a Bearer-token control would have nothing to wire up.
 */
export function useRunConfig(): RunConfig {
  const [endpoint, setEndpoint] = useState('');
  const [fhirVersion, setFhirVersion] = useState<FhirVersion>('R4');
  const selection = useSuiteSelection();

  return {
    endpoint,
    setEndpoint,
    targetUrl: endpoint.trim() === '' ? '' : `https://${endpoint.trim()}`,
    fhirVersion,
    setFhirVersion,
    selection,
  };
}
