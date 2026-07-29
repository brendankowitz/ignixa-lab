import { useEffect, useRef, useState } from 'react';
import { runSearch } from './searchApi';
import type { SearchRequest, SearchTraceResponse } from './searchTypes';
import { getErrorMessage } from '../shared/errorMessage';

const DEBOUNCE_MS = 450;

export interface SearchTraceState {
  result: SearchTraceResponse | null;
  error: string | null;
  isLoading: boolean;
}

const EMPTY: SearchTraceState = { result: null, error: null, isLoading: false };

/** Debounced, abortable search-trace runner: re-GETs ~450ms after `request` changes, cancelling any
 * still-in-flight request first. `request === null` means "not enough input to run yet" (e.g. compartment/
 * $everything mode with no id typed) -- clears to the empty state without calling the API. */
export function useSearchTrace(request: SearchRequest | null): SearchTraceState {
  const [state, setState] = useState<SearchTraceState>(EMPTY);
  const abortRef = useRef<AbortController | null>(null);
  const requestKey = request ? JSON.stringify(request) : null;

  useEffect(() => {
    if (request === null) {
      abortRef.current?.abort();
      setState(EMPTY);
      return undefined;
    }

    const timer = setTimeout(() => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;
      setState((prev) => ({ ...prev, isLoading: true }));

      runSearch(request, controller.signal)
        .then((result) => setState({ result, error: null, isLoading: false }))
        .catch((error: unknown) => {
          // Ask our own controller, not the error's name. Any AbortError we did not cause (a browser-imposed
          // abort on navigation, or a timeout signal composed in later) would otherwise take this early
          // return and strand `isLoading: true` forever, since no new effect run follows to clear it.
          if (controller.signal.aborted) {
            return;
          }
          setState({ result: null, error: getErrorMessage(error), isLoading: false });
        });
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      abortRef.current?.abort();
    };
    // `requestKey` is the stable, content-based re-run trigger; `request` itself is a fresh object every
    // render even when unchanged, so depending on it directly would re-run the effect every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [requestKey]);

  return state;
}
