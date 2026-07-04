/** A generic FHIR `Parameters.parameter[]` entry — permissive enough to cover every `value[x]`/`resource`/`part` shape the backend emits. */
export interface FhirParameter {
  name: string;
  part?: FhirParameter[];
  resource?: unknown;
  [valueKey: string]: unknown;
}

export interface FhirParameters {
  resourceType: string;
  parameter?: FhirParameter[];
}

export interface FmlRequestInput {
  mapText: string;
  resourceText: string;
}

export interface FmlEvalResult {
  error: string | null;
  evaluator: string;
  /** Pretty-printed JSON text of the transform's target resource, or `null` before any run. */
  output: string | null;
  /** `log(...)` lines captured during the transform, in emission order. */
  trace: string[];
  /** Rule-level error diagnostics from the debug `outcome` part (non-fatal — the transform still produced `output`). */
  outcomeIssues: string[];
}

const EMPTY_RESULT: FmlEvalResult = { error: null, evaluator: '', output: null, trace: [], outcomeIssues: [] };

/** Builds the FHIR `Parameters` request body for `StructureMap/$transform`. Throws if `resourceText` isn't valid JSON — callers should catch and surface it as a resource-JSON error. */
export function buildFmlRequest(input: FmlRequestInput): FhirParameters {
  return {
    resourceType: 'Parameters',
    parameter: [
      { name: 'map', valueString: input.mapText },
      { name: 'resource', resource: JSON.parse(input.resourceText) },
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

/** POSTs to the FML transform endpoint (with `debug=true` so rule-level errors are also surfaced via the `outcome` part) and returns the raw `Parameters` response. Throws if the body is a top-level `OperationOutcome` (a hard failure — parse error, unresolved model reference, or unhandled exception, always HTTP 400), the response is non-2xx, or on a network/abort error. */
export async function runFml(body: FhirParameters, signal: AbortSignal): Promise<FhirParameters> {
  const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
  const response = await fetch(`${apiBaseUrl}/api/StructureMap/$transform?debug=true`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  });

  const text = await response.text();
  let json: FhirParameters;
  try {
    json = JSON.parse(text) as FhirParameters;
  } catch {
    throw new Error(`Request failed with status ${response.status} ${response.statusText}`);
  }

  const operationOutcomeMessage = readOperationOutcomeMessage(json);
  if (!response.ok || operationOutcomeMessage !== null) {
    throw new Error(operationOutcomeMessage ?? `Request failed with status ${response.status}`);
  }
  return json;
}

/** Parses the FML transform endpoint's `Parameters` response into the shape `FmlBench.tsx` renders. */
export function parseFmlResponse(response: FhirParameters): FmlEvalResult {
  const parameters = response.parameter ?? [];

  const configPart = parameters.find((parameter) => parameter.name === 'parameters');
  const evaluatorPart = configPart?.part?.find((part) => part.name === 'evaluator');
  const evaluator = (evaluatorPart?.valueString as string) ?? '';

  const trace = parameters
    .filter((parameter) => parameter.name === 'trace')
    .map((parameter) => (parameter.valueString as string) ?? '');

  const resultPart = parameters.find((parameter) => parameter.name === 'result');
  // Normalize CRLF -> LF: the backend pretty-prints JSON with `\r\n`, but `diffLines` splits on `\n` only,
  // so every line would otherwise show as changed vs. the (LF-only) expected text.
  const output = typeof resultPart?.valueString === 'string' ? resultPart.valueString.replace(/\r\n/g, '\n') : null;

  const outcomePart = parameters.find((parameter) => parameter.name === 'outcome');
  const outcomeResource = outcomePart?.resource as
    | { issue?: { severity?: string; diagnostics?: string; details?: { text?: string } }[] }
    | undefined;
  const outcomeIssues = (outcomeResource?.issue ?? [])
    .filter((issue) => issue.severity === 'error')
    .map((issue) => issue.diagnostics?.trim() || issue.details?.text?.trim() || 'Unknown rule error');

  return { ...EMPTY_RESULT, evaluator, output, trace, outcomeIssues };
}
