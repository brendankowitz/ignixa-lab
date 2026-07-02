import { useCallback, useState } from 'react';

/** State and actions for managing a set of selected suite IDs. */
export interface SuiteSelection {
  /** The set of currently selected suite IDs. */
  selected: Set<string>;
  /** Toggles a single suite ID in or out of the selection. */
  toggle: (suiteId: string) => void;
  /** Selects or clears every suite from `allIds`. */
  toggleAll: (allIds: readonly string[], select: boolean) => void;
  /** Adds or removes a subset of IDs, leaving the rest of the selection intact (used for group toggles). */
  setMany: (ids: readonly string[], select: boolean) => void;
  /** Clears the selection. */
  clear: () => void;
}

/** Manages the set of selected suite IDs for the picker. */
export function useSuiteSelection(): SuiteSelection {
  const [selected, setSelected] = useState<Set<string>>(new Set());

  const toggle = useCallback((suiteId: string) => {
    setSelected((previous) => {
      const next = new Set(previous);
      if (next.has(suiteId)) {
        next.delete(suiteId);
      } else {
        next.add(suiteId);
      }
      return next;
    });
  }, []);

  const toggleAll = useCallback((allIds: readonly string[], select: boolean) => {
    setSelected(select ? new Set(allIds) : new Set());
  }, []);

  const setMany = useCallback((ids: readonly string[], select: boolean) => {
    setSelected((previous) => {
      const next = new Set(previous);
      for (const id of ids) {
        if (select) {
          next.add(id);
        } else {
          next.delete(id);
        }
      }
      return next;
    });
  }, []);

  const clear = useCallback(() => setSelected(new Set()), []);

  return { selected, toggle, toggleAll, setMany, clear };
}
