import type { SearchTraceResponse } from './searchTypes';

/** GETs the search-trace endpoint for a resource type + raw query string. Throws on non-2xx or a
 * `{ error }` body (the backend reports bad requests that way), or on a network/abort error. */
export async function runSearch(
  resourceType: string,
  query: string,
  signal: AbortSignal,
): Promise<SearchTraceResponse> {
  const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
  const suffix = query.trim() ? `?${query.trim()}` : '';
  const response = await fetch(`${apiBaseUrl}/api/search/${encodeURIComponent(resourceType)}${suffix}`, {
    method: 'GET',
    signal,
  });

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
