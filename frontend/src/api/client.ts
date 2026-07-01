import type {
  ConformanceReport,
  HealthResponse,
  RunRequest,
  SuiteDescriptor,
} from '../types/conformance';

/**
 * Base URL for the Functions backend. In development this is proxied by Vite
 * (see `vite.config.ts`); in production it can be overridden at build time with
 * the `VITE_API_BASE_URL` environment variable.
 */
const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

/** Error thrown when an API call returns a non-2xx response. */
export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });

  if (!response.ok) {
    throw new ApiError(response.status, await readError(response));
  }

  return (await response.json()) as T;
}

/** Extracts a human-readable message from an error response body. */
async function readError(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { error?: string };
    if (body.error) {
      return body.error;
    }
  } catch {
    // Body was not JSON; fall back to the status text below.
  }
  return response.statusText || `Request failed with status ${response.status}`;
}

/** Liveness probe reporting service status and engine version. */
export function getHealth(signal?: AbortSignal): Promise<HealthResponse> {
  return request<HealthResponse>('/api/health', { signal });
}

/** Retrieves the catalog of bundled TestScript suites available to run. */
export function getSuites(signal?: AbortSignal): Promise<SuiteDescriptor[]> {
  return request<SuiteDescriptor[]>('/api/suites', { signal });
}

/** Executes the selected suites against the target server. */
export function runConformance(
  body: RunRequest,
  signal?: AbortSignal,
): Promise<ConformanceReport> {
  return request<ConformanceReport>('/api/run', {
    method: 'POST',
    body: JSON.stringify(body),
    signal,
  });
}
