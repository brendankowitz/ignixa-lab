/** A generic FHIR `Parameters.parameter[]` entry — permissive enough to cover every `resource`/`valueString` shape used by this endpoint. */
export interface FhirParameter {
  name: string;
  resource?: unknown;
  [valueKey: string]: unknown;
}

export interface FhirParameters {
  resourceType: string;
  parameter?: FhirParameter[];
}

export type SofCellValue = string | number | boolean | null;

export interface SofEvalResult {
  error: string | null;
  columns: string[];
  rows: Record<string, SofCellValue>[];
  meta: string;
}

export interface SofRequestInput {
  viewDefinitionText: string;
  resourcesText: string;
}

const EMPTY_RESULT: SofEvalResult = { error: null, columns: [], rows: [], meta: '' };

/** Builds the FHIR `Parameters` request body for `ViewDefinition/$viewdefinition-run`. `resourcesText` may be a single resource object or a JSON array of resources — either way, each one becomes its own `resource` parameter. Throws if either JSON text field is invalid — callers should catch and surface it as a JSON error. */
export function buildSofRequest(input: SofRequestInput): FhirParameters {
  const viewResource = JSON.parse(input.viewDefinitionText);
  const resourcesValue: unknown = JSON.parse(input.resourcesText);
  const resourceList = Array.isArray(resourcesValue) ? resourcesValue : [resourcesValue];

  return {
    resourceType: 'Parameters',
    parameter: [
      { name: 'viewResource', resource: viewResource },
      ...resourceList.map((resource) => ({ name: 'resource', resource })),
    ],
  };
}

/** Extracts a readable message from an `OperationOutcome` error response, if that's what the body is. */
function readOperationOutcomeMessage(body: unknown): string | null {
  const outcome = body as { resourceType?: string; issue?: { details?: { text?: string }; diagnostics?: string }[] };
  if (outcome?.resourceType !== 'OperationOutcome' || !outcome.issue?.length) {
    return null;
  }
  const issue = outcome.issue[0];
  return issue.details?.text?.trim() || issue.diagnostics?.trim() || null;
}

/** POSTs to the SQL-on-FHIR view runner and returns the raw JSON-array response. Throws if the body is an `OperationOutcome` (the backend reports every error this way, always with a non-2xx status), the response is non-2xx, or on a network/abort error. */
export async function runSof(body: FhirParameters, signal: AbortSignal): Promise<unknown[]> {
  const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
  const response = await fetch(`${apiBaseUrl}/api/ViewDefinition/$viewdefinition-run`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  });

  const text = await response.text();
  let json: unknown;
  try {
    json = text ? JSON.parse(text) : [];
  } catch {
    throw new Error(`Request failed with status ${response.status} ${response.statusText}`);
  }

  const operationOutcomeMessage = readOperationOutcomeMessage(json);
  if (!response.ok || operationOutcomeMessage !== null) {
    throw new Error(operationOutcomeMessage ?? `Request failed with status ${response.status}`);
  }
  // A 2xx body that is neither a row array nor a recognized OperationOutcome is a
  // contract violation - surface it instead of silently returning an empty table.
  if (!Array.isArray(json)) {
    throw new Error('Unexpected response shape from ViewDefinition endpoint');
  }
  return json;
}

/** Parses the SQL-on-FHIR runner's JSON-array response into the table shape `SofBench.tsx` renders — column order is the union of keys in first-seen order across all rows. */
export function parseSofResponse(rows: unknown[]): SofEvalResult {
  const columns: string[] = [];
  const tableRows: Record<string, SofCellValue>[] = [];

  for (const row of rows) {
    const record = (row ?? {}) as Record<string, unknown>;
    const tableRow: Record<string, SofCellValue> = {};
    for (const key of Object.keys(record)) {
      if (!columns.includes(key)) columns.push(key);
      const value = record[key];
      tableRow[key] = typeof value === 'object' && value !== null ? JSON.stringify(value) : (value as SofCellValue);
    }
    tableRows.push(tableRow);
  }

  const meta = `${tableRows.length} ${tableRows.length === 1 ? 'row' : 'rows'} · ${columns.length} cols`;
  return { ...EMPTY_RESULT, columns, rows: tableRows, meta };
}
