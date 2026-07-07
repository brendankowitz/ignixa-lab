import { useEffect, useMemo, useState } from 'react';
import { errorMessage } from '../shared/errorMessage';
import { validateResource } from './validationApi';
import type { ValidationDepth, ValidationResponse } from './validationTypes';

export interface UseValidationRunInput {
  fhirVersion: string;
  depth: ValidationDepth;
  skipTerminology: boolean;
  packageText: string;
  resourceText: string;
}

export interface ValidationRunState {
  result: ValidationResponse | null;
  error: string | null;
  isLoading: boolean;
}

export function parsePackageText(packageText: string): string[] {
  return packageText
    .split(/\r?\n|,/)
    .map((value) => value.trim())
    .filter(Boolean);
}

export function useValidationRun(input: UseValidationRunInput): ValidationRunState {
  const [state, setState] = useState<ValidationRunState>({ result: null, error: null, isLoading: false });
  const packages = useMemo(() => parsePackageText(input.packageText), [input.packageText]);

  useEffect(() => {
    const controller = new AbortController();
    let resource: unknown;
    try {
      resource = JSON.parse(input.resourceText);
    } catch (error) {
      setState({ result: null, error: `Invalid JSON: ${errorMessage(error)}`, isLoading: false });
      return () => controller.abort();
    }

    setState((current) => ({ ...current, error: null, isLoading: true }));
    const timeout = window.setTimeout(() => {
      validateResource(
        {
          fhirVersion: input.fhirVersion,
          depth: input.depth,
          skipTerminology: input.skipTerminology,
          packages,
          resource,
        },
        controller.signal,
      )
        .then((result) => setState({ result, error: null, isLoading: false }))
        .catch((error) => {
          if (!controller.signal.aborted) {
            setState({ result: null, error: errorMessage(error), isLoading: false });
          }
        });
    }, 350);

    return () => {
      window.clearTimeout(timeout);
      controller.abort();
    };
  }, [input.depth, input.fhirVersion, input.resourceText, input.skipTerminology, packages]);

  return state;
}
