/**
 * Curated blurb text for edge-case categories, keyed by the real category id
 * `Ignixa.FhirFakes`'s `EdgeCaseCatalog` reports. Wording is carried over from
 * the original design mockup where the id matches; categories with no entry
 * here fall back to a humanized version of their id (see `describeEdgeCase`).
 */
const EDGE_CASE_DESCRIPTIONS: Record<string, string> = {
  'unicode.cjk': 'CJK (Chinese/Japanese/Korean) characters',
  'unicode.rtl': 'Right-to-left script (Arabic / Hebrew)',
  'unicode.combining': 'Appends combining diacritical marks',
  'unicode.emoji': 'Emoji incl. ZWJ sequences & surrogate pairs',
  'unicode.zero-width': 'Injects zero-width chars (U+200B/C/D, U+FEFF)',
  'unicode.multi-script-long': '~40-fragment Latin + CJK + RTL + Cyrillic + emoji',
  'temporal.leap-year': 'Sets the date to Feb 29 of a leap year',
  'temporal.year-boundary': 'Sets the date to Dec 31 or Jan 1',
  'temporal.far-past': 'Far-past but spec-valid date (0001-01-01)',
  'temporal.far-future': 'Far-future but spec-valid date (9999-12-31)',
  'temporal.partial-precision': 'Reduces to year-only or year-month precision',
  'string.max-length': 'Replaces text with a very long ASCII string',
  'string.injection-like': 'SQL / HTML / template-injection-like payloads',
  'string.control-chars': 'Injects C0 control characters (disallowed by grammar)',
  'string.whitespace-only': 'Sets text to whitespace-only',
  'string.empty-present': 'Sets text to empty string (invalid per spec)',
};

/** Humanizes a category id like `string.max-length` into `Max Length` as a fallback when no curated entry exists. */
function humanize(categoryId: string): string {
  const leaf = categoryId.includes('.') ? categoryId.split('.').slice(1).join('.') : categoryId;
  return leaf
    .split(/[-.]/)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');
}

export function describeEdgeCase(categoryId: string): string {
  return EDGE_CASE_DESCRIPTIONS[categoryId] ?? humanize(categoryId);
}
