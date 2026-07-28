import type { SearchRequest, SearchTraceResponse } from './searchTypes';

function apiBaseUrl(): string {
  return (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
}

function buildUrl(request: SearchRequest): string {
  const base = apiBaseUrl();
  switch (request.mode) {
    case 'type': {
      const suffix = request.query.trim() ? `?${request.query.trim()}` : '';
      return `${base}/api/search/${encodeURIComponent(request.fhirVersion)}/${encodeURIComponent(request.resourceType)}${suffix}`;
    }
    case 'compartment': {
      const suffix = request.query.trim() ? `?${request.query.trim()}` : '';
      return `${base}/api/search/${encodeURIComponent(request.fhirVersion)}/${encodeURIComponent(request.compartmentType)}/${encodeURIComponent(request.compartmentId)}/${encodeURIComponent(request.memberType)}${suffix}`;
    }
    case 'everything': {
      const params = new URLSearchParams();
      if (request.typeFilter.length > 0) {
        params.set('_type', request.typeFilter.join(','));
      }
      if (request.since.trim()) {
        params.set('_since', request.since.trim());
      }
      if (request.start.trim()) {
        params.set('start', request.start.trim());
      }
      if (request.end.trim()) {
        params.set('end', request.end.trim());
      }
      if (!request.includeReferencedResources) {
        params.set('includeReferencedResources', 'false');
      }
      const suffix = params.toString() ? `?${params.toString()}` : '';
      // "$everything" is a literal route segment, not encoded -- the backend route defines it the same way.
      return `${base}/api/search/${encodeURIComponent(request.fhirVersion)}/Patient/${encodeURIComponent(request.patientId)}/$everything${suffix}`;
    }
  }
}

/** GETs the search-trace endpoint for a `SearchRequest` (type search, compartment search, or $everything).
 * Throws on non-2xx or a `{ error }` body (the backend reports bad requests that way), or on a network/abort
 * error. */
export async function runSearch(request: SearchRequest, signal: AbortSignal): Promise<SearchTraceResponse> {
  const response = await fetch(buildUrl(request), { method: 'GET', signal });

  const text = await response.text();
  let json: unknown;
  try {
    json = JSON.parse(text);
  } catch {
    throw new Error(`Request failed with status ${response.status} ${response.statusText}`);
  }

  if (!response.ok) {
    const errorBody = json as { error?: string };
    throw new Error(errorBody?.error ?? `Request failed with status ${response.status}`);
  }
  return json as SearchTraceResponse;
}
