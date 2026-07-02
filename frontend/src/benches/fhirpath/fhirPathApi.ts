import type {
  FhirParameter,
  FhirParameters,
  FhirVersion,
  FpAstNode,
  FpEvalResult,
  FpResultItem,
  FpVariable,
} from './fhirPathTypes';

const ROUTE_BY_VERSION: Record<FhirVersion, string> = {
  stu3: '$fhirpath-stu3',
  r4: '$fhirpath-r4',
  r4b: '$fhirpath-r4b',
  r5: '$fhirpath-r5',
  r6: '$fhirpath-r6',
};

const JSON_VALUE_EXTENSION_URL = 'http://fhir.forms-lab.com/StructureDefinition/json-value';

export interface FhirPathRequestInput {
  expression: string;
  context: string;
  resourceText: string;
  variables: FpVariable[];
}

/** Builds the FHIR `Parameters` request body per the wire format documented in docs/superpowers/specs/2026-07-02-expression-benches-design.md. Throws if `resourceText` isn't valid JSON — callers should catch and surface it as a resource-JSON error. */
export function buildFhirPathRequest(input: FhirPathRequestInput): FhirParameters {
  const parameter: FhirParameter[] = [{ name: 'expression', valueString: input.expression }];

  if (input.context.trim()) {
    parameter.push({ name: 'context', valueString: input.context });
  }

  const namedVariables = input.variables.filter((variable) => variable.name.trim());
  if (namedVariables.length > 0) {
    parameter.push({
      name: 'variables',
      part: namedVariables.map((variable) => ({ name: variable.name, valueString: variable.value })),
    });
  }

  parameter.push({ name: 'resource', resource: JSON.parse(input.resourceText) });

  return { resourceType: 'Parameters', parameter };
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

/** POSTs to the FHIRPath evaluator route for the given version and returns the raw `Parameters` response. Throws if the body is an `OperationOutcome` (the backend reports evaluation/parse errors this way even on HTTP 200) or the response is non-2xx, or on a network/abort error. */
export async function runFhirPath(
  version: FhirVersion,
  body: FhirParameters,
  signal: AbortSignal,
): Promise<FhirParameters> {
  const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');
  const response = await fetch(`${apiBaseUrl}/api/${ROUTE_BY_VERSION[version]}`, {
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

/** Reads a result/trace part's value, preferring `resource`, then the json-value extension, then `value{Type}`, in that priority order. */
function readPartValue(part: FhirParameter): string {
  if (part.resource !== undefined) {
    return JSON.stringify(part.resource, null, 2);
  }

  const jsonExtension = part.extension?.find((extension) => extension.url === JSON_VALUE_EXTENSION_URL);
  if (jsonExtension?.valueString !== undefined) {
    try {
      return JSON.stringify(JSON.parse(jsonExtension.valueString), null, 2);
    } catch {
      return jsonExtension.valueString;
    }
  }

  const valueKey = Object.keys(part).find((key) => key.startsWith('value'));
  if (valueKey) {
    const value = part[valueKey];
    return typeof value === 'object' && value !== null ? JSON.stringify(value, null, 2) : JSON.stringify(value);
  }

  return '';
}

function toResultItem(part: FhirParameter): FpResultItem {
  return { type: part.name, text: readPartValue(part) };
}

interface RawAstNode {
  ExpressionType: string;
  Name: string;
  ReturnType?: string;
  Arguments?: RawAstNode[];
}

function parseAstNode(raw: RawAstNode): FpAstNode {
  return {
    expressionType: raw.ExpressionType,
    name: raw.Name,
    returnType: raw.ReturnType ?? null,
    arguments: Array.isArray(raw.Arguments) ? raw.Arguments.map(parseAstNode) : [],
  };
}

/** Parses the FHIRPath evaluator's `Parameters` response into the shape the bench UI renders. */
export function parseFhirPathResponse(response: FhirParameters): FpEvalResult {
  const parameters = response.parameter ?? [];
  const emptyResult: FpEvalResult = { error: null, evaluator: '', groups: [], trace: [], ast: null, astParseFailed: false };

  const errorParameter = parameters.find((parameter) => parameter.name === 'error');
  if (errorParameter) {
    return { ...emptyResult, error: (errorParameter.valueString as string) ?? 'Unknown error' };
  }

  const configPart = parameters.find((parameter) => parameter.name === 'parameters');
  const evaluatorPart = configPart?.part?.find((part) => part.name === 'evaluator');
  const evaluator = (evaluatorPart?.valueString as string) ?? '';

  const astPart = configPart?.part?.find((part) => part.name === 'parseDebugTree');
  let ast: FpAstNode | null = null;
  let astParseFailed = false;
  if (typeof astPart?.valueString === 'string') {
    try {
      ast = parseAstNode(JSON.parse(astPart.valueString) as RawAstNode);
    } catch (error) {
      console.warn('Failed to parse parseDebugTree payload', error, astPart.valueString);
      ast = null;
      astParseFailed = true;
    }
  }

  const groups = [];
  const trace = [];
  for (const resultParameter of parameters.filter((parameter) => parameter.name === 'result')) {
    const items: FpResultItem[] = [];
    for (const part of resultParameter.part ?? []) {
      if (part.name === 'trace') {
        trace.push({ label: (part.valueString as string) ?? '', items: (part.part ?? []).map(toResultItem) });
      } else {
        items.push(toResultItem(part));
      }
    }
    groups.push({ label: (resultParameter.valueString as string) ?? null, items });
  }

  return { error: null, evaluator, groups, trace, ast, astParseFailed };
}
