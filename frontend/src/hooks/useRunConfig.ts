import { useState } from 'react';
import { useSuiteSelection, type SuiteSelection } from './useSuiteSelection';

/** Public shape returned by {@link useRunConfig}. */
export interface RunConfig {
  /** Bare host + path the user typed, without the `https://` prefix (e.g. `hapi.fhir.org/baseR4`). */
  endpoint: string;
  /** Updates the bare endpoint. */
  setEndpoint: (endpoint: string) => void;
  /** Full `https://` target URL, or an empty string until an endpoint is entered. */
  targetUrl: string;
  /** Suite selection state, shared with the Setup screen's suite checklist. */
  selection: SuiteSelection;
}

export interface InitialRunConfig {
  targetUrl?: string;
  suiteIds?: string[];
}

/**
 * Matches a leading `http://` or `https://` a user may have typed or pasted
 * into the endpoint field. The field always renders a fixed `https://`
 * prefix, so any scheme the user included would otherwise be prepended a
 * second time (`https://http://host`), producing a URL whose host is the
 * literal string `http` — see `useRunConfig.ts` targetUrl construction.
 */
const LEADING_SCHEME = /^https?:\/\//i;

function endpointFromTargetUrl(targetUrl: string | undefined): string {
  return targetUrl?.replace(LEADING_SCHEME, '') ?? '';
}

/**
 * Owns the Setup screen's run configuration: target endpoint and selected
 * suite IDs. There is no FHIR version field — that's detected per-run from
 * the target's own CapabilityStatement, not chosen up front. Deliberately
 * holds no auth field either — `RunRequest` carries none, so a Bearer-token
 * control would have nothing to wire up.
 */
export function useRunConfig(initial: InitialRunConfig = {}): RunConfig {
  // Preserve the original scheme from the share-link-decoded targetUrl.
  // If initial URL was explicit `http://`, keep it; otherwise default to `https://`.
  const initialScheme = /^http:\/\//i.test(initial.targetUrl ?? '') ? 'http://' : 'https://';

  const [endpoint, setEndpointRaw] = useState(() => endpointFromTargetUrl(initial.targetUrl));
  const selection = useSuiteSelection(initial.suiteIds);

  // Strip any scheme the user typed or pasted so it can never combine with
  // the field's fixed `https://` prefix into a malformed double-scheme URL
  // (e.g. pasting `http://hapi.fhir.org/baseR4` must not become
  // `https://http://hapi.fhir.org/baseR4`, which resolves to host `http`).
  const setEndpoint = (value: string) => setEndpointRaw(value.replace(LEADING_SCHEME, ''));

  return {
    endpoint,
    setEndpoint,
    targetUrl: endpoint.trim() === '' ? '' : `${initialScheme}${endpoint.trim()}`,
    selection,
  };
}
