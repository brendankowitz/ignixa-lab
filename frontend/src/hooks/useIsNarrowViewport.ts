import { useEffect, useState } from 'react';

/**
 * True while the viewport is at or below `maxWidthPx`. The bench screens lay
 * out their editor/result panes as inline styles (no stylesheet, so no media
 * queries) — this hook is how those layouts switch from a fixed-minimum
 * two-column grid on desktop to a single stacked column on narrow screens.
 */
export function useIsNarrowViewport(maxWidthPx: number): boolean {
  const query = `(max-width: ${maxWidthPx}px)`;
  const [matches, setMatches] = useState(() => window.matchMedia(query).matches);

  useEffect(() => {
    const mql = window.matchMedia(query);
    const handleChange = (event: MediaQueryListEvent) => setMatches(event.matches);
    setMatches(mql.matches);
    mql.addEventListener('change', handleChange);
    return () => mql.removeEventListener('change', handleChange);
  }, [query]);

  return matches;
}
