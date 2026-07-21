/** One `key=value` pair from a raw FHIR search query string. Order is preserved — a FHIR search query
 * string has no ordering requirement, but preserving insertion order keeps the textarea's contents stable
 * (a builder action never reshuffles pairs the user didn't touch). */
export interface QueryPair {
  key: string;
  value: string;
}

/** Splits a raw query string (the part after `?`, e.g. `name=Smith&gender=male`) into its `key=value`
 * pairs. Tolerant of a leading `?`, trailing `&`, and blank segments (all of which a hand-edited textarea
 * can produce) — none of those should throw or corrupt the round trip. */
export function parseQueryString(query: string): QueryPair[] {
  const trimmed = query.trim().replace(/^\?/, '');
  if (trimmed.length === 0) {
    return [];
  }
  return trimmed
    .split('&')
    .filter((segment) => segment.length > 0)
    .map((segment) => {
      const eq = segment.indexOf('=');
      return eq === -1 ? { key: segment, value: '' } : { key: segment.slice(0, eq), value: segment.slice(eq + 1) };
    });
}

/** Joins `key=value` pairs back into a raw query string, `&`-separated, no leading `?`. */
export function toQueryString(pairs: QueryPair[]): string {
  return pairs.map(({ key, value }) => `${key}=${value}`).join('&');
}

/** Sets a single-valued control parameter (`_summary`, `_total`, `_count`, …): replaces the pair with this
 * exact `key` if one exists, otherwise appends it. A blank `value` removes the key entirely instead of
 * adding `key=` — "unset this control" should look like it was never added, not like an empty value. */
export function upsertSingleton(query: string, key: string, value: string): string {
  const pairs = parseQueryString(query).filter((p) => p.key !== key);
  if (value.trim().length > 0) {
    pairs.push({ key, value });
  }
  return toQueryString(pairs);
}

/** Removes a single-valued control parameter entirely, if present. */
export function removeKey(query: string, key: string): string {
  return toQueryString(parseQueryString(query).filter((p) => p.key !== key));
}

/** Appends a repeatable parameter (`_include`, `_revinclude`, …) unless the exact same `key=value` pair is
 * already present — repeatable parameters can appear more than once with *different* values (multiple
 * `_include`s), but adding the identical one twice is a no-op, not a duplicate. */
export function appendUnique(query: string, key: string, value: string): string {
  const pairs = parseQueryString(query);
  if (pairs.some((p) => p.key === key && p.value === value)) {
    return query;
  }
  pairs.push({ key, value });
  return toQueryString(pairs);
}

/** Removes one specific repeatable-parameter pair (the exact `key=value`, not every pair with that key —
 * unlike {@link removeKey}, since `_include`/`_revinclude` can legitimately have several different values
 * at once and removing "one of them" must not remove the others). */
export function removePair(query: string, key: string, value: string): string {
  return toQueryString(parseQueryString(query).filter((p) => !(p.key === key && p.value === value)));
}

/** One `_sort` slot: a search-parameter code plus its direction. FHIR encodes descending as a `-` prefix
 * on the field name within the single comma-joined `_sort` value (the engine supports up to 3 keys). */
export interface SortField {
  field: string;
  descending: boolean;
}

/** Replaces the query's `_sort` value with the given fields (empty `fields` removes `_sort` entirely) —
 * always a full replace, never an append, since `_sort` is one parameter whose value is a comma list, not
 * a repeatable key like `_include`. Blank `field` entries are dropped (an unfilled builder slot). */
export function setSort(query: string, fields: SortField[]): string {
  const value = fields
    .filter((f) => f.field.trim().length > 0)
    .map((f) => `${f.descending ? '-' : ''}${f.field}`)
    .join(',');
  return value.length > 0 ? upsertSingleton(query, '_sort', value) : removeKey(query, '_sort');
}
