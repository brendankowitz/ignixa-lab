import type { SearchRequest, SearchTraceResponse } from './searchTypes';

function apiBaseUrl(): string {
  // Optional-chained on `.env` itself (not just the property) so buildUrl stays callable under the plain
  // `node --experimental-strip-types --test` runner searchApi.test.ts uses -- Vite always populates
  // `import.meta.env`, but a bare Node ESM loader leaves it undefined rather than an empty object, and
  // `undefined.VITE_API_BASE_URL` throws before the `?? ''` below ever gets a chance to run.
  return (import.meta.env?.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
}

// Exported for direct unit testing (searchApi.test.ts) -- three branches, five conditional query params, and
// a deliberately-unencoded "$everything" segment are enough surface area to warrant testing the URL shape in
// isolation rather than only indirectly through runSearch/fetch.
export function buildUrl(request: SearchRequest): string {
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

/** How much of an unparseable body to quote back. Enough to recognize an HTML error page or a proxy notice,
 * short enough to stay readable in an error banner. */
const BODY_EXCERPT_LENGTH = 200;

/** GETs the search-trace endpoint for a `SearchRequest` (type search, compartment search, or $everything).
 * Throws on a network/abort error, on a non-2xx (using the backend's `{ error }` body where present), and on
 * a 2xx whose body isn't a search trace. */
export async function runSearch(request: SearchRequest, signal: AbortSignal): Promise<SearchTraceResponse> {
  const response = await fetch(buildUrl(request), { method: 'GET', signal });

  const text = await response.text();
  let json: unknown;
  try {
    json = JSON.parse(text);
  } catch (parseError) {
    // Quote the body rather than only the status. The status alone produces self-contradicting messages: a
    // misconfigured VITE_API_BASE_URL sends /api/search/... to the SPA's own index.html fallback, which
    // answers 200 with HTML -- reported as "Request failed with status 200 OK", true and useless.
    const excerpt = text.slice(0, BODY_EXCERPT_LENGTH);
    throw new Error(
      response.ok
        ? `Expected a search trace but got a non-JSON response (HTTP ${response.status}). Check VITE_API_BASE_URL. Body starts: ${excerpt}`
        : `Request failed with status ${response.status} ${response.statusText}: ${excerpt}`,
      { cause: parseError },
    );
  }

  if (!response.ok) {
    const errorBody = json as { error?: string };
    throw new Error(errorBody?.error ?? `Request failed with status ${response.status}`);
  }

  // A 2xx of the wrong shape would otherwise be cast blind and blow up mid-render (`result.parameters.map`),
  // which unmounts the bench to a blank page instead of showing the error banner -- the throw happens in
  // React's render phase, where useSearchTrace's catch can't see it. One structural check is enough to turn
  // that into an ordinary reported error.
  const trace = json as SearchTraceResponse;
  if (!trace || typeof trace !== 'object' || !Array.isArray(trace.parameters)) {
    throw new Error(
      `Expected a search trace but got ${JSON.stringify(json).slice(0, BODY_EXCERPT_LENGTH)}`,
    );
  }
  return trace;
}
