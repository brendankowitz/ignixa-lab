import { useCallback, useEffect, useState } from 'react';
import type { CSSProperties } from 'react';
import { THEME_STORAGE_KEY, THEME_VARIABLES, type ThemeMode } from '../theme/variables';

/** Public shape returned by {@link useTheme}. */
export interface ThemeState {
  /** The active theme mode. */
  theme: ThemeMode;
  /** Switches between light and dark, persisting the choice. */
  toggle: () => void;
  /** CSS custom properties for the active theme, ready to spread onto a wrapper element's `style`. */
  variables: CSSProperties;
  /** Glyph for the theme-toggle button (☾ in light mode, ☀ in dark mode). */
  icon: string;
}

/** Reads the persisted theme, falling back to `dark` for unset/invalid values. */
function readStoredTheme(): ThemeMode {
  const stored = localStorage.getItem(THEME_STORAGE_KEY);
  return stored === 'dark' || stored === 'light' ? stored : 'dark';
}

/**
 * Owns light/dark theme state, persisted to `localStorage` under the same key
 * the design mockup uses ({@link THEME_STORAGE_KEY}), and exposes the matching
 * CSS custom-property set from {@link THEME_VARIABLES}.
 */
export function useTheme(): ThemeState {
  const [theme, setTheme] = useState<ThemeMode>(readStoredTheme);

  useEffect(() => {
    localStorage.setItem(THEME_STORAGE_KEY, theme);
  }, [theme]);

  const toggle = useCallback(() => {
    setTheme((previous) => (previous === 'dark' ? 'light' : 'dark'));
  }, []);

  return {
    theme,
    toggle,
    // CSS custom properties aren't part of React's CSSProperties type; the cast
    // is the standard escape hatch for applying a `--token` map as inline style.
    variables: THEME_VARIABLES[theme] as CSSProperties,
    icon: theme === 'dark' ? '☀' : '☾',
  };
}
