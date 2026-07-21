import { useEffect, useRef, useState } from 'react';
import { runSearch } from './searchApi';
import type { SearchTraceResponse } from './searchTypes';
import { getErrorMessage } from '../shared/errorMessage';

const DEBOUNCE_MS = 450;

export interface SearchTraceState {
  result: SearchTraceResponse | null;
  error: string | null;
  isLoading: boolean;
}

const EMPTY: SearchTraceState = { result: null, error: null, isLoading: false };

/** Debounced, abortable search-trace runner: re-GETs ~450ms after the last change to resourceType/query,
 * cancelling any still-in-flight request first. */
export function useSearchTrace(resourceType: string, query: string): SearchTraceState {
  const [state, setState] = useState<SearchTraceState>(EMPTY);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;
      setState((prev) => ({ ...prev, isLoading: true }));

      runSearch(resourceType, query, controller.signal)
        .then((result) => setState({ result, error: null, isLoading: false }))
        .catch((error: unknown) => {
          if (error instanceof DOMException && error.name === 'AbortError') {
            return;
          }
          setState({ result: null, error: getErrorMessage(error), isLoading: false });
        });
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      abortRef.current?.abort();
    };
  }, [resourceType, query]);

  return state;
}
