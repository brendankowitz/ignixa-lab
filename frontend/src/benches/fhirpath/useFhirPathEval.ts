import { useEffect, useRef, useState } from 'react';
import { buildFhirPathRequest, parseFhirPathResponse, runFhirPath } from './fhirPathApi';
import type { FhirVersion, FpEvalResult, FpVariable } from './fhirPathTypes';

export interface FhirPathEvalInput {
  version: FhirVersion;
  expression: string;
  context: string;
  resourceText: string;
  variables: FpVariable[];
}

const DEBOUNCE_MS = 450;

const EMPTY_RESULT: FpEvalResult = { error: null, evaluator: '', groups: [], trace: [], ast: null };

/** Debounced, abortable evaluator: re-POSTs to the FHIRPath backend ~450ms after the last change to any input field, cancelling any still-in-flight request first. */
export function useFhirPathEval(input: FhirPathEvalInput): { result: FpEvalResult; isLoading: boolean } {
  const [result, setResult] = useState<FpEvalResult>(EMPTY_RESULT);
  const [isLoading, setIsLoading] = useState(false);
  const abortControllerRef = useRef<AbortController | null>(null);

  const variablesKey = JSON.stringify(input.variables);

  useEffect(() => {
    if (!input.expression.trim()) {
      abortControllerRef.current?.abort();
      setResult(EMPTY_RESULT);
      setIsLoading(false);
      return;
    }

    const timer = setTimeout(() => {
      abortControllerRef.current?.abort();
      const controller = new AbortController();
      abortControllerRef.current = controller;
      setIsLoading(true);

      let body;
      try {
        body = buildFhirPathRequest(input);
      } catch (error) {
        setIsLoading(false);
        setResult({ ...EMPTY_RESULT, error: `Resource JSON — ${(error as Error).message}` });
        return;
      }

      runFhirPath(input.version, body, controller.signal)
        .then((response) => setResult(parseFhirPathResponse(response)))
        .catch((error: Error) => {
          if (error.name === 'AbortError') {
            return;
          }
          setResult({ ...EMPTY_RESULT, error: error.message });
        })
        .finally(() => {
          if (abortControllerRef.current === controller) {
            setIsLoading(false);
          }
        });
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
    // input.variables is a fresh array each render; variablesKey is the stable dependency for it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [input.version, input.expression, input.context, input.resourceText, variablesKey]);

  return { result, isLoading };
}
