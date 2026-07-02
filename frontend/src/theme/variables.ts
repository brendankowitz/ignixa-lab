/**
 * Design tokens lifted verbatim from the "Ignixa Lab" design mockup
 * (`Ignixa Lab.dc.html`, claude.ai Design project `8903721c-aa03-4d16-8e8d-6ccad7d9b096`).
 *
 * The mockup supports three accent families (violet/ember/teal) and a
 * words-vs-icons chip toggle; per the frontend design spec, v1 ships
 * violet-only, so only the violet ramp is carried over.
 */

/** Theme mode, persisted to `localStorage` under {@link THEME_STORAGE_KEY}. */
export type ThemeMode = 'light' | 'dark';

/** `localStorage` key for the persisted theme — matches the design mockup's key exactly. */
export const THEME_STORAGE_KEY = 'ignixa-lab-theme';

/** Violet accent ramp (main / soft fill / border / shadow / gradient), per theme. */
const ACCENT_VIOLET = {
  light: {
    main: '#7c3aed',
    soft: 'rgba(124,58,237,.07)',
    border: 'rgba(124,58,237,.28)',
    shadow: '0 2px 8px rgba(124,58,237,.25)',
    grad: 'linear-gradient(90deg,#7c3aed,#d6336c)',
  },
  dark: {
    main: '#a855f7',
    soft: 'rgba(168,85,247,.12)',
    border: 'rgba(168,85,247,.35)',
    shadow: '0 2px 10px rgba(168,85,247,.35)',
    grad: 'linear-gradient(90deg,#a855f7,#d6336c)',
  },
} as const;

/**
 * Full CSS custom-property set for each theme mode. Applied as inline styles
 * on the app's root wrapper element by {@link useTheme}, so every descendant
 * can reference `var(--token)` regardless of which theme is active.
 */
export const THEME_VARIABLES: Record<ThemeMode, Record<string, string>> = {
  light: {
    '--bg': '#fbfbfc',
    '--panel': '#ffffff',
    '--panel2': '#f8f7fa',
    '--inset': '#f5f4f7',
    '--code': '#ffffff',
    '--border': 'rgba(31,26,38,.09)',
    '--border2': 'rgba(31,26,38,.14)',
    '--text': '#1f1a26',
    '--text2': '#4c4358',
    '--text3': '#9a93a6',
    '--text4': '#b3adc0',
    '--accent': ACCENT_VIOLET.light.main,
    '--accent-soft': ACCENT_VIOLET.light.soft,
    '--accent-border': ACCENT_VIOLET.light.border,
    '--accent-contrast': '#fff',
    '--accent-shadow': ACCENT_VIOLET.light.shadow,
    '--grad': ACCENT_VIOLET.light.grad,
    '--pass': '#15803d',
    '--pass-dot': '#16a34a',
    '--pass-bg': '#e9f7ee',
    '--fail': '#b91c1c',
    '--fail-dot': '#dc2626',
    '--fail-bg': '#fdeaea',
    '--fail-soft': '#fdf5f5',
    '--fail-border': 'rgba(220,38,38,.25)',
    '--skip': '#9a93a6',
    '--cov-full': '#2f9e44',
    '--cov-part': '#69bd7b',
    '--cov-fail': '#e8590c',
    '--shadow': '0 1px 3px rgba(31,26,38,.05)',
  },
  dark: {
    '--bg': '#161318',
    '--panel': '#1d1922',
    '--panel2': '#1a1620',
    '--inset': '#241f2d',
    '--code': '#131017',
    '--border': 'rgba(231,228,236,.07)',
    '--border2': 'rgba(231,228,236,.12)',
    '--text': '#e7e4ec',
    '--text2': '#c9c3d4',
    '--text3': '#8b8494',
    '--text4': '#615a6d',
    '--accent': ACCENT_VIOLET.dark.main,
    '--accent-soft': ACCENT_VIOLET.dark.soft,
    '--accent-border': ACCENT_VIOLET.dark.border,
    '--accent-contrast': '#fff',
    '--accent-shadow': ACCENT_VIOLET.dark.shadow,
    '--grad': ACCENT_VIOLET.dark.grad,
    '--pass': '#4ade80',
    '--pass-dot': '#4ade80',
    '--pass-bg': 'rgba(74,222,128,.12)',
    '--fail': '#f87171',
    '--fail-dot': '#f87171',
    '--fail-bg': 'rgba(248,113,113,.15)',
    '--fail-soft': 'rgba(248,113,113,.07)',
    '--fail-border': 'rgba(248,113,113,.3)',
    '--skip': '#8b8494',
    '--cov-full': '#22c55e',
    '--cov-part': 'rgba(34,197,94,.45)',
    '--cov-fail': '#fb923c',
    '--shadow': 'none',
  },
};
