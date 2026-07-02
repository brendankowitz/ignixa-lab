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
 * Matches a leading `http://` or `https://` a user may have typed or pasted
 * into the endpoint field. The field always renders a fixed `https://`
 * prefix, so any scheme the user included would otherwise be prepended a
 * second time (`https://http://host`), producing a URL whose host is the
 * literal string `http` — see `useRunConfig.ts` targetUrl construction.
 */
const LEADING_SCHEME = /^https?:\/\//i;

/**
 * Owns the Setup screen's run configuration: target endpoint, FHIR version,
 * and selected suite IDs. Deliberately holds no auth field — `RunRequest`
 * carries none, so a Bearer-token control would have nothing to wire up.
 */
export function useRunConfig(): RunConfig {
  const [endpoint, setEndpointRaw] = useState('');
  const [fhirVersion, setFhirVersion] = useState<FhirVersion>('R4');
  const selection = useSuiteSelection();

  // Strip any scheme the user typed or pasted so it can never combine with
  // the field's fixed `https://` prefix into a malformed double-scheme URL
  // (e.g. pasting `http://hapi.fhir.org/baseR4` must not become
  // `https://http://hapi.fhir.org/baseR4`, which resolves to host `http`).
  const setEndpoint = (value: string) => setEndpointRaw(value.replace(LEADING_SCHEME, ''));

  return {
    endpoint,
    setEndpoint,
    targetUrl: endpoint.trim() === '' ? '' : `https://${endpoint.trim()}`,
    fhirVersion,
    setFhirVersion,
    selection,
  };
}
